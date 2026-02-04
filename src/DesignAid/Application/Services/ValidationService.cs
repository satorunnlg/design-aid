using Microsoft.EntityFrameworkCore;
using DesignAid.Domain.Entities;
using DesignAid.Domain.Standards;
using DesignAid.Domain.ValueObjects;
using DesignAid.Infrastructure.Persistence;

namespace DesignAid.Application.Services;

/// <summary>
/// 設計基準に基づくバリデーションを行うサービス。
/// </summary>
public class ValidationService
{
    private readonly DesignAidDbContext _context;
    private readonly HashService _hashService;
    private readonly List<IDesignStandard> _standards;

    /// <summary>
    /// ValidationService を初期化する。
    /// </summary>
    public ValidationService(DesignAidDbContext context, HashService hashService)
    {
        _context = context;
        _hashService = hashService;
        _standards = LoadStandards();
    }

    /// <summary>
    /// 設計基準を読み込む。
    /// </summary>
    private static List<IDesignStandard> LoadStandards()
    {
        return new List<IDesignStandard>
        {
            new MaterialStandard(),
            new ToleranceStandard()
        };
    }

    /// <summary>
    /// 利用可能な設計基準一覧を取得する。
    /// </summary>
    public IReadOnlyList<IDesignStandard> GetAvailableStandards()
    {
        return _standards.AsReadOnly();
    }

    /// <summary>
    /// 特定の設計基準を取得する。
    /// </summary>
    public IDesignStandard? GetStandard(string standardId)
    {
        return _standards.FirstOrDefault(s =>
            s.StandardId.Equals(standardId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 全パーツを検証する。
    /// </summary>
    public async Task<VerificationResult> VerifyAllAsync(
        string? standardId = null,
        CancellationToken ct = default)
    {
        var result = new VerificationResult();
        var parts = await _context.Parts.ToListAsync(ct);

        foreach (var part in parts)
        {
            var partResult = await VerifyPartAsync(part, standardId, ct);
            result.Merge(partResult);
        }

        return result;
    }

    /// <summary>
    /// 特定のパーツを検証する。
    /// </summary>
    public async Task<VerificationResult> VerifyPartAsync(
        DesignComponent part,
        string? standardId = null,
        CancellationToken ct = default)
    {
        var result = new VerificationResult();

        // 整合性検証
        var integrityResult = await VerifyIntegrityAsync(part, ct);
        result.AddResult(part.PartNumber.Value, integrityResult);

        // 設計基準検証
        var standardsToCheck = standardId != null
            ? _standards.Where(s => s.StandardId.Equals(standardId, StringComparison.OrdinalIgnoreCase))
            : _standards;

        foreach (var standard in standardsToCheck)
        {
            var standardResult = standard.Validate(part);
            result.AddResult(part.PartNumber.Value, standardResult);
        }

        return result;
    }

    /// <summary>
    /// パーツの整合性を検証する。
    /// </summary>
    public Task<ValidationResult> VerifyIntegrityAsync(
        DesignComponent part,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var details = new List<ValidationDetail>();

        // ディレクトリの存在確認
        if (!Directory.Exists(part.DirectoryPath))
        {
            return Task.FromResult(ValidationResult.Error(
                $"ディレクトリが存在しません: {part.DirectoryPath}",
                part.PartNumber));
        }

        // 成果物ファイルの検証
        foreach (var (relativePath, expectedHash) in part.ArtifactHashes)
        {
            var fullPath = Path.Combine(part.DirectoryPath, relativePath);

            if (!File.Exists(fullPath))
            {
                details.Add(new ValidationDetail(
                    relativePath,
                    $"ファイルが見つかりません: {relativePath}",
                    ValidationSeverity.Error));
                continue;
            }

            var actualHash = _hashService.ComputeHash(fullPath);
            if (actualHash.Value != expectedHash.Value)
            {
                details.Add(new ValidationDetail(
                    relativePath,
                    $"ハッシュが一致しません（最後の同期以降に変更されています）",
                    ValidationSeverity.Warning));
            }
        }

        if (details.Count == 0)
        {
            return Task.FromResult(ValidationResult.Ok(part.PartNumber));
        }

        var maxSeverity = details.Max(d => d.Severity);
        return Task.FromResult(ValidationResult.WithDetails(
            maxSeverity,
            $"パーツ '{part.PartNumber}' に問題があります",
            details,
            part.PartNumber));
    }

    /// <summary>
    /// パーツ番号で検証する。
    /// </summary>
    public async Task<VerificationResult> VerifyByPartNumberAsync(
        string partNumber,
        string? standardId = null,
        CancellationToken ct = default)
    {
        var part = await _context.Parts
            .FirstOrDefaultAsync(p => p.PartNumber == new PartNumber(partNumber), ct);

        if (part == null)
        {
            var result = new VerificationResult();
            result.AddResult(partNumber, ValidationResult.Error(
                $"パーツが見つかりません: {partNumber}",
                new PartNumber(partNumber)));
            return result;
        }

        return await VerifyPartAsync(part, standardId, ct);
    }

    /// <summary>
    /// 特定のプロジェクト内のパーツを検証する。
    /// </summary>
    public async Task<VerificationResult> VerifyByProjectAsync(
        Guid projectId,
        string? standardId = null,
        CancellationToken ct = default)
    {
        var result = new VerificationResult();

        var partIds = await _context.AssetComponents
            .Include(ac => ac.Asset)
            .Where(ac => ac.Asset != null && ac.Asset.ProjectId == projectId)
            .Select(ac => ac.PartId)
            .Distinct()
            .ToListAsync(ct);

        var parts = await _context.Parts
            .Where(p => partIds.Contains(p.Id))
            .ToListAsync(ct);

        foreach (var part in parts)
        {
            var partResult = await VerifyPartAsync(part, standardId, ct);
            result.Merge(partResult);
        }

        return result;
    }

    /// <summary>
    /// 特定の装置内のパーツを検証する。
    /// </summary>
    public async Task<VerificationResult> VerifyByAssetAsync(
        Guid assetId,
        string? standardId = null,
        CancellationToken ct = default)
    {
        var result = new VerificationResult();

        var partIds = await _context.AssetComponents
            .Where(ac => ac.AssetId == assetId)
            .Select(ac => ac.PartId)
            .ToListAsync(ct);

        var parts = await _context.Parts
            .Where(p => partIds.Contains(p.Id))
            .ToListAsync(ct);

        foreach (var part in parts)
        {
            var partResult = await VerifyPartAsync(part, standardId, ct);
            result.Merge(partResult);
        }

        return result;
    }
}

/// <summary>
/// 検証結果。
/// </summary>
public class VerificationResult
{
    /// <summary>パーツごとの検証結果</summary>
    public Dictionary<string, List<ValidationResult>> Results { get; } = new();

    /// <summary>合格数</summary>
    public int PassCount => Results.Count(r => r.Value.All(v => v.IsSuccess));

    /// <summary>警告数</summary>
    public int WarningCount => Results.Count(r =>
        r.Value.Any(v => v.HasWarnings) && !r.Value.Any(v => v.HasErrors));

    /// <summary>失敗数</summary>
    public int FailCount => Results.Count(r => r.Value.Any(v => v.HasErrors));

    /// <summary>
    /// 結果を追加する。
    /// </summary>
    public void AddResult(string partNumber, ValidationResult result)
    {
        if (!Results.ContainsKey(partNumber))
        {
            Results[partNumber] = new List<ValidationResult>();
        }
        Results[partNumber].Add(result);
    }

    /// <summary>
    /// 別の結果をマージする。
    /// </summary>
    public void Merge(VerificationResult other)
    {
        foreach (var (key, values) in other.Results)
        {
            if (!Results.ContainsKey(key))
            {
                Results[key] = new List<ValidationResult>();
            }
            Results[key].AddRange(values);
        }
    }

    /// <summary>
    /// 特定のパーツの結果を取得する。
    /// </summary>
    public IReadOnlyList<ValidationResult> GetResults(string partNumber)
    {
        return Results.TryGetValue(partNumber, out var results)
            ? results.AsReadOnly()
            : Array.Empty<ValidationResult>();
    }

    /// <summary>
    /// 全体の最大重要度を取得する。
    /// </summary>
    public ValidationSeverity GetMaxSeverity()
    {
        if (Results.Count == 0) return ValidationSeverity.Ok;

        var allResults = Results.Values.SelectMany(r => r);
        if (allResults.Any(r => r.HasErrors)) return ValidationSeverity.Error;
        if (allResults.Any(r => r.HasWarnings)) return ValidationSeverity.Warning;
        return ValidationSeverity.Ok;
    }
}
