using System.Text;
using DesignAid.Domain.Entities;
using DesignAid.Domain.ValueObjects;

namespace DesignAid.Application.Services;

/// <summary>
/// ファイルハッシュ計算・検証サービスのインターフェース。
/// </summary>
public interface IHashService
{
    /// <summary>
    /// ファイルの SHA256 ハッシュを計算する。
    /// </summary>
    FileHash ComputeHash(string filePath);

    /// <summary>
    /// ファイルの SHA256 ハッシュを非同期で計算する。
    /// </summary>
    Task<FileHash> ComputeHashAsync(string filePath, CancellationToken ct = default);

    /// <summary>
    /// ストリームの SHA256 ハッシュを計算する。
    /// </summary>
    FileHash ComputeHash(Stream stream);

    /// <summary>
    /// バイト配列の SHA256 ハッシュを計算する。
    /// </summary>
    FileHash ComputeHash(byte[] data);

    /// <summary>
    /// 文字列の SHA256 ハッシュを計算する。
    /// </summary>
    FileHash ComputeHash(string text, Encoding? encoding = null);

    /// <summary>
    /// ファイルのハッシュが期待値と一致するか検証する。
    /// </summary>
    bool VerifyHash(string filePath, FileHash expectedHash);

    /// <summary>
    /// ファイルのハッシュが期待値と一致するか検証する（非同期）。
    /// </summary>
    Task<bool> VerifyHashAsync(string filePath, FileHash expectedHash, CancellationToken ct = default);

    /// <summary>
    /// パーツの整合性を検証する。
    /// </summary>
    ValidationResult ValidateComponentIntegrity(DesignComponent component);

    /// <summary>
    /// パーツの整合性を検証する（非同期）。
    /// </summary>
    Task<ValidationResult> ValidateComponentIntegrityAsync(
        DesignComponent component,
        CancellationToken ct = default);

    /// <summary>
    /// ディレクトリ内の全ファイルのハッシュを計算する。
    /// </summary>
    Dictionary<string, FileHash> ComputeDirectoryHashes(
        string directoryPath,
        string searchPattern = "*.*");

    /// <summary>
    /// 複数ハッシュを結合して単一のハッシュを生成する。
    /// </summary>
    FileHash CombineHashes(IEnumerable<FileHash> hashes);

    /// <summary>
    /// パーツの成果物から CurrentHash を計算する。
    /// </summary>
    string ComputeCurrentHash(DesignComponent component);
}
