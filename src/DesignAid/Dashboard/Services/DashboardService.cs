using Microsoft.EntityFrameworkCore;
using DesignAid.Application.DTOs;
using DesignAid.Application.Services;
using DesignAid.Domain.Entities;
using DesignAid.Domain.ValueObjects;
using DesignAid.Infrastructure.Persistence;

namespace DesignAid.Dashboard.Services;

/// <summary>
/// ダッシュボード画面用のデータ集約サービス。
/// </summary>
public class DashboardService
{
    private readonly IDbContextFactory<DesignAidDbContext> _contextFactory;
    private readonly ISearchService? _searchService;

    public DashboardService(
        IDbContextFactory<DesignAidDbContext> contextFactory,
        ISearchService? searchService = null)
    {
        _contextFactory = contextFactory;
        _searchService = searchService;
    }

    /// <summary>
    /// ダッシュボードのサマリー情報を取得する。
    /// </summary>
    public async Task<DashboardSummary> GetSummaryAsync(CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);

        var assets = await context.Assets.ToListAsync(ct);
        var parts = await context.Parts.ToListAsync(ct);

        var summary = new DashboardSummary
        {
            AssetCount = assets.Count,
            PartCount = parts.Count,
            TypeSummary = new TypeSummary
            {
                Fabricated = parts.Count(p => p.Type == PartType.Fabricated),
                Purchased = parts.Count(p => p.Type == PartType.Purchased),
                Standard = parts.Count(p => p.Type == PartType.Standard)
            },
            StatusSummary = new StatusSummary
            {
                Draft = parts.Count(p => p.Status == HandoverStatus.Draft),
                Ordered = parts.Count(p => p.Status == HandoverStatus.Ordered),
                Delivered = parts.Count(p => p.Status == HandoverStatus.Delivered),
                Canceled = parts.Count(p => p.Status == HandoverStatus.Canceled)
            },
            RecentParts = parts
                .OrderByDescending(p => p.UpdatedAt)
                .Take(5)
                .Select(p => new PartSummaryItem
                {
                    Id = p.Id,
                    PartNumber = p.PartNumber.Value,
                    Name = p.Name,
                    Type = p.Type switch
                    {
                        PartType.Fabricated => "製作物",
                        PartType.Purchased => "購入品",
                        PartType.Standard => "規格品",
                        _ => p.Type.ToString()
                    },
                    Status = p.Status.ToDisplayName(),
                    UpdatedAt = p.UpdatedAt
                })
                .ToList()
        };

        return summary;
    }

    /// <summary>
    /// パーツ一覧を取得する。
    /// </summary>
    public async Task<List<PartSummaryItem>> GetPartsAsync(
        PartType? typeFilter = null,
        HandoverStatus? statusFilter = null,
        string? searchText = null,
        CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);

        // フィルタリング・ソートはメモリ上で実行（PartNumber 値オブジェクト・Type 抽象プロパティ対応）
        var parts = await context.Parts.ToListAsync(ct);

        if (typeFilter.HasValue)
            parts = parts.Where(p => p.Type == typeFilter.Value).ToList();

        if (statusFilter.HasValue)
            parts = parts.Where(p => p.Status == statusFilter.Value).ToList();

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            parts = parts.Where(p =>
                p.PartNumber.Value.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                p.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                (p.Memo ?? "").Contains(searchText, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        parts = parts.OrderBy(p => p.PartNumber.Value, StringComparer.OrdinalIgnoreCase).ToList();

        return parts.Select(p => new PartSummaryItem
        {
            Id = p.Id,
            PartNumber = p.PartNumber.Value,
            Name = p.Name,
            Type = FormatPartType(p.Type),
            Status = p.Status.ToDisplayName(),
            UpdatedAt = p.UpdatedAt
        }).ToList();
    }

    /// <summary>
    /// 装置一覧を取得する。
    /// </summary>
    public async Task<List<AssetSummaryItem>> GetAssetsAsync(CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);

        var assets = await context.Assets
            .Include(a => a.AssetComponents)
            .OrderBy(a => a.Name)
            .ToListAsync(ct);

        return assets.Select(a => new AssetSummaryItem
        {
            Id = a.Id,
            Name = a.Name,
            DisplayName = a.DisplayName,
            Description = a.Description,
            PartCount = a.AssetComponents.Count,
            CreatedAt = a.CreatedAt,
            UpdatedAt = a.UpdatedAt
        }).ToList();
    }

    /// <summary>
    /// 装置の詳細（パーツ一覧含む）を取得する。
    /// </summary>
    public async Task<AssetDetailItem?> GetAssetDetailAsync(Guid assetId, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);

        var asset = await context.Assets
            .Include(a => a.AssetComponents)
                .ThenInclude(ac => ac.Part)
            .FirstOrDefaultAsync(a => a.Id == assetId, ct);

        if (asset is null) return null;

        return new AssetDetailItem
        {
            Id = asset.Id,
            Name = asset.Name,
            DisplayName = asset.DisplayName,
            Description = asset.Description,
            DirectoryPath = asset.DirectoryPath,
            CreatedAt = asset.CreatedAt,
            UpdatedAt = asset.UpdatedAt,
            Parts = asset.AssetComponents
                .Where(ac => ac.Part != null)
                .Select(ac => new AssetPartItem
                {
                    PartId = ac.PartId,
                    PartNumber = ac.Part!.PartNumber.Value,
                    Name = ac.Part.Name,
                    Type = FormatPartType(ac.Part.Type),
                    Status = ac.Part.Status.ToDisplayName(),
                    Quantity = ac.Quantity,
                    Notes = ac.Notes
                })
                .OrderBy(p => p.PartNumber)
                .ToList()
        };
    }

    /// <summary>
    /// 整合性チェックを実行する。
    /// </summary>
    public async Task<List<IntegrityCheckItem>> RunIntegrityCheckAsync(CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var hashService = new HashService();

        var parts = await context.Parts.ToListAsync(ct);
        var results = new List<IntegrityCheckItem>();

        foreach (var part in parts)
        {
            var item = new IntegrityCheckItem
            {
                PartNumber = part.PartNumber.Value,
                Name = part.Name,
                Type = FormatPartType(part.Type),
                Status = part.Status.ToDisplayName()
            };

            if (!Directory.Exists(part.DirectoryPath))
            {
                item.CheckStatus = "Error";
                item.Message = $"ディレクトリが存在しません: {part.DirectoryPath}";
                results.Add(item);
                continue;
            }

            var issues = new List<string>();
            foreach (var (relativePath, expectedHash) in part.ArtifactHashes)
            {
                var fullPath = Path.Combine(part.DirectoryPath, relativePath);
                if (!File.Exists(fullPath))
                {
                    issues.Add($"ファイルが見つかりません: {relativePath}");
                    item.CheckStatus = "Error";
                    continue;
                }

                var actualHash = hashService.ComputeHash(fullPath);
                if (actualHash.Value != expectedHash.Value)
                {
                    issues.Add($"変更検知: {relativePath}");
                    if (item.CheckStatus != "Error")
                        item.CheckStatus = "Warning";
                }
            }

            if (issues.Count == 0)
            {
                item.CheckStatus = "Ok";
                item.Message = "整合性OK";
            }
            else
            {
                item.Message = string.Join("; ", issues);
            }

            results.Add(item);
        }

        return results;
    }

    /// <summary>
    /// 類似パーツを検索する。
    /// </summary>
    public async Task<List<SearchHitItem>> SearchPartsAsync(
        string query,
        CancellationToken ct = default)
    {
        if (_searchService is null)
            throw new InvalidOperationException(
                "検索サービスが利用できません。ベクトルインデックスが構築されていない可能性があります。");

        var results = await _searchService.SearchAsync(query, threshold: 0.5, limit: 20, ct: ct);

        return results.Select(r => new SearchHitItem
        {
            PartNumber = r.PartNumber,
            Name = r.Content,
            Type = "",
            Status = "",
            Score = r.Score
        }).ToList();
    }

    private static string FormatPartType(PartType type) => type switch
    {
        PartType.Fabricated => "製作物",
        PartType.Purchased => "購入品",
        PartType.Standard => "規格品",
        _ => type.ToString()
    };
}

/// <summary>
/// ダッシュボードサマリー DTO。
/// </summary>
public class DashboardSummary
{
    /// <summary>装置数</summary>
    public int AssetCount { get; set; }

    /// <summary>パーツ数</summary>
    public int PartCount { get; set; }

    /// <summary>種別別集計</summary>
    public TypeSummary TypeSummary { get; set; } = new();

    /// <summary>ステータス別集計</summary>
    public StatusSummary StatusSummary { get; set; } = new();

    /// <summary>最近更新されたパーツ</summary>
    public List<PartSummaryItem> RecentParts { get; set; } = [];
}

/// <summary>
/// パーツサマリー項目（ダッシュボード表示用）。
/// </summary>
public class PartSummaryItem
{
    public Guid Id { get; set; }
    public string PartNumber { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// 装置サマリー項目。
/// </summary>
public class AssetSummaryItem
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public int PartCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// 装置詳細項目。
/// </summary>
public class AssetDetailItem
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public string DirectoryPath { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<AssetPartItem> Parts { get; set; } = [];
}

/// <summary>
/// 装置内パーツ項目。
/// </summary>
public class AssetPartItem
{
    public Guid PartId { get; set; }
    public string PartNumber { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public string? Notes { get; set; }
}

/// <summary>
/// 整合性チェック結果項目。
/// </summary>
public class IntegrityCheckItem
{
    public string PartNumber { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string CheckStatus { get; set; } = "Ok";
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// 検索結果項目。
/// </summary>
public class SearchHitItem
{
    public string PartNumber { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public float Score { get; set; }
}
