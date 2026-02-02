using System.Security.Cryptography;
using System.Text;
using DesignAid.Domain.Entities;
using DesignAid.Domain.Exceptions;
using DesignAid.Domain.ValueObjects;

namespace DesignAid.Application.Services;

/// <summary>
/// ファイルハッシュの計算・検証を行うサービス。
/// </summary>
public class HashService
{
    /// <summary>
    /// ファイルの SHA256 ハッシュを計算する。
    /// </summary>
    /// <param name="filePath">ファイルパス</param>
    /// <returns>FileHash</returns>
    /// <exception cref="IntegrityException">ファイルが見つからない場合</exception>
    public FileHash ComputeHash(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw IntegrityException.FileNotFound(filePath);
        }

        using var stream = File.OpenRead(filePath);
        var hashBytes = SHA256.HashData(stream);
        return FileHash.FromBytes(hashBytes);
    }

    /// <summary>
    /// ファイルの SHA256 ハッシュを非同期で計算する。
    /// </summary>
    /// <param name="filePath">ファイルパス</param>
    /// <param name="ct">キャンセルトークン</param>
    /// <returns>FileHash</returns>
    public async Task<FileHash> ComputeHashAsync(string filePath, CancellationToken ct = default)
    {
        if (!File.Exists(filePath))
        {
            throw IntegrityException.FileNotFound(filePath);
        }

        await using var stream = File.OpenRead(filePath);
        var hashBytes = await SHA256.HashDataAsync(stream, ct);
        return FileHash.FromBytes(hashBytes);
    }

    /// <summary>
    /// ストリームの SHA256 ハッシュを計算する。
    /// </summary>
    /// <param name="stream">入力ストリーム</param>
    /// <returns>FileHash</returns>
    public FileHash ComputeHash(Stream stream)
    {
        var hashBytes = SHA256.HashData(stream);
        return FileHash.FromBytes(hashBytes);
    }

    /// <summary>
    /// バイト配列の SHA256 ハッシュを計算する。
    /// </summary>
    /// <param name="data">バイト配列</param>
    /// <returns>FileHash</returns>
    public FileHash ComputeHash(byte[] data)
    {
        var hashBytes = SHA256.HashData(data);
        return FileHash.FromBytes(hashBytes);
    }

    /// <summary>
    /// 文字列の SHA256 ハッシュを計算する。
    /// </summary>
    /// <param name="text">文字列</param>
    /// <returns>FileHash</returns>
    public FileHash ComputeHash(string text, Encoding? encoding = null)
    {
        var bytes = (encoding ?? Encoding.UTF8).GetBytes(text);
        return ComputeHash(bytes);
    }

    /// <summary>
    /// ファイルのハッシュが期待値と一致するか検証する。
    /// </summary>
    /// <param name="filePath">ファイルパス</param>
    /// <param name="expectedHash">期待されるハッシュ</param>
    /// <returns>一致する場合は true</returns>
    public bool VerifyHash(string filePath, FileHash expectedHash)
    {
        if (!File.Exists(filePath))
            return false;

        var actualHash = ComputeHash(filePath);
        return actualHash == expectedHash;
    }

    /// <summary>
    /// ファイルのハッシュが期待値と一致するか検証する（非同期）。
    /// </summary>
    public async Task<bool> VerifyHashAsync(string filePath, FileHash expectedHash, CancellationToken ct = default)
    {
        if (!File.Exists(filePath))
            return false;

        var actualHash = await ComputeHashAsync(filePath, ct);
        return actualHash == expectedHash;
    }

    /// <summary>
    /// パーツの整合性を検証する。
    /// </summary>
    /// <param name="component">検証対象のパーツ</param>
    /// <returns>バリデーション結果</returns>
    public ValidationResult ValidateComponentIntegrity(DesignComponent component)
    {
        var details = new List<ValidationDetail>();

        foreach (var (relativePath, expectedHash) in component.ArtifactHashes)
        {
            var fullPath = Path.Combine(component.DirectoryPath, relativePath);

            if (!File.Exists(fullPath))
            {
                details.Add(new ValidationDetail(
                    relativePath,
                    $"ファイルが見つかりません: {relativePath}",
                    ValidationSeverity.Error));
                continue;
            }

            try
            {
                var actualHash = ComputeHash(fullPath);
                if (actualHash != expectedHash)
                {
                    details.Add(new ValidationDetail(
                        relativePath,
                        $"ハッシュ不整合: 期待={expectedHash.ToHexString()[..8]}..., 実際={actualHash.ToHexString()[..8]}...",
                        ValidationSeverity.Warning));
                }
                else
                {
                    details.Add(new ValidationDetail(
                        relativePath,
                        "ハッシュ一致",
                        ValidationSeverity.Ok));
                }
            }
            catch (Exception ex) when (ex is not IntegrityException)
            {
                details.Add(new ValidationDetail(
                    relativePath,
                    $"ハッシュ計算エラー: {ex.Message}",
                    ValidationSeverity.Error));
            }
        }

        if (details.Count == 0)
        {
            return ValidationResult.Ok(component.PartNumber);
        }

        var maxSeverity = details.Max(d => d.Severity);
        var message = maxSeverity switch
        {
            ValidationSeverity.Error => $"パーツ '{component.PartNumber}' に整合性エラーがあります",
            ValidationSeverity.Warning => $"パーツ '{component.PartNumber}' のファイルが変更されています",
            _ => $"パーツ '{component.PartNumber}' は正常です"
        };

        return ValidationResult.WithDetails(maxSeverity, message, details, component.PartNumber);
    }

    /// <summary>
    /// パーツの整合性を検証する（非同期）。
    /// </summary>
    public async Task<ValidationResult> ValidateComponentIntegrityAsync(
        DesignComponent component,
        CancellationToken ct = default)
    {
        var details = new List<ValidationDetail>();

        foreach (var (relativePath, expectedHash) in component.ArtifactHashes)
        {
            var fullPath = Path.Combine(component.DirectoryPath, relativePath);

            if (!File.Exists(fullPath))
            {
                details.Add(new ValidationDetail(
                    relativePath,
                    $"ファイルが見つかりません: {relativePath}",
                    ValidationSeverity.Error));
                continue;
            }

            try
            {
                var actualHash = await ComputeHashAsync(fullPath, ct);
                if (actualHash != expectedHash)
                {
                    details.Add(new ValidationDetail(
                        relativePath,
                        $"ハッシュ不整合: 期待={expectedHash.ToHexString()[..8]}..., 実際={actualHash.ToHexString()[..8]}...",
                        ValidationSeverity.Warning));
                }
                else
                {
                    details.Add(new ValidationDetail(
                        relativePath,
                        "ハッシュ一致",
                        ValidationSeverity.Ok));
                }
            }
            catch (Exception ex) when (ex is not IntegrityException)
            {
                details.Add(new ValidationDetail(
                    relativePath,
                    $"ハッシュ計算エラー: {ex.Message}",
                    ValidationSeverity.Error));
            }
        }

        if (details.Count == 0)
        {
            return ValidationResult.Ok(component.PartNumber);
        }

        var maxSeverity = details.Max(d => d.Severity);
        var message = maxSeverity switch
        {
            ValidationSeverity.Error => $"パーツ '{component.PartNumber}' に整合性エラーがあります",
            ValidationSeverity.Warning => $"パーツ '{component.PartNumber}' のファイルが変更されています",
            _ => $"パーツ '{component.PartNumber}' は正常です"
        };

        return ValidationResult.WithDetails(maxSeverity, message, details, component.PartNumber);
    }

    /// <summary>
    /// ディレクトリ内の全ファイルのハッシュを計算する。
    /// </summary>
    /// <param name="directoryPath">ディレクトリパス</param>
    /// <param name="searchPattern">検索パターン（デフォルト: *.*）</param>
    /// <returns>相対パスとハッシュの辞書</returns>
    public Dictionary<string, FileHash> ComputeDirectoryHashes(
        string directoryPath,
        string searchPattern = "*.*")
    {
        if (!Directory.Exists(directoryPath))
        {
            throw new DirectoryNotFoundException($"ディレクトリが見つかりません: {directoryPath}");
        }

        var result = new Dictionary<string, FileHash>();

        foreach (var filePath in Directory.EnumerateFiles(directoryPath, searchPattern, SearchOption.AllDirectories))
        {
            // part.json は除外（別途管理）
            var fileName = Path.GetFileName(filePath);
            if (fileName.Equals("part.json", StringComparison.OrdinalIgnoreCase))
                continue;

            var relativePath = Path.GetRelativePath(directoryPath, filePath);
            var hash = ComputeHash(filePath);
            result[relativePath] = hash;
        }

        return result;
    }

    /// <summary>
    /// 複数ハッシュを結合して単一のハッシュを生成する。
    /// パーツの全成果物を代表する CurrentHash として使用。
    /// </summary>
    /// <param name="hashes">ハッシュのコレクション</param>
    /// <returns>結合ハッシュ</returns>
    public FileHash CombineHashes(IEnumerable<FileHash> hashes)
    {
        // ソートして一貫性を保証
        var sortedHashes = hashes
            .Select(h => h.Value)
            .OrderBy(h => h)
            .ToList();

        if (sortedHashes.Count == 0)
        {
            // 空の場合は空文字列のハッシュ
            return ComputeHash(Array.Empty<byte>());
        }

        var combined = string.Join(":", sortedHashes);
        return ComputeHash(combined, Encoding.ASCII);
    }

    /// <summary>
    /// パーツの成果物から CurrentHash を計算する。
    /// </summary>
    /// <param name="component">パーツ</param>
    /// <returns>結合ハッシュ文字列</returns>
    public string ComputeCurrentHash(DesignComponent component)
    {
        return CombineHashes(component.ArtifactHashes.Values);
    }
}
