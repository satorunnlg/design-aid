using Microsoft.EntityFrameworkCore;
using DesignAid.Domain.Entities;
using DesignAid.Infrastructure.Persistence;
using DesignAid.Infrastructure.Qdrant;

namespace DesignAid.Application.Services;

/// <summary>
/// 類似設計検索を行うサービス。
/// </summary>
public class SearchService
{
    private readonly DesignAidDbContext? _context;
    private readonly QdrantService? _qdrantService;

    /// <summary>
    /// SearchService を初期化する。
    /// </summary>
    /// <param name="context">DB コンテキスト（オプション）</param>
    /// <param name="qdrantService">Qdrant サービス（オプション）</param>
    public SearchService(DesignAidDbContext? context = null, QdrantService? qdrantService = null)
    {
        _context = context;
        _qdrantService = qdrantService;
    }

    /// <summary>
    /// Qdrant が利用可能かどうかを確認する。
    /// </summary>
    public async Task<bool> IsQdrantAvailableAsync(CancellationToken ct = default)
    {
        if (_qdrantService == null) return false;
        return await _qdrantService.CheckConnectionAsync(ct);
    }

    /// <summary>
    /// パーツをベクトルDBに同期する。
    /// </summary>
    /// <param name="partId">パーツID（nullの場合は全パーツ）</param>
    /// <param name="ct">キャンセルトークン</param>
    /// <returns>同期したパーツ数</returns>
    public async Task<int> SyncToVectorDbAsync(Guid? partId = null, CancellationToken ct = default)
    {
        if (_context == null || _qdrantService == null)
        {
            return 0;
        }

        var partsQuery = _context.Parts.AsQueryable();
        if (partId.HasValue)
        {
            partsQuery = partsQuery.Where(p => p.Id == partId.Value);
        }

        var parts = await partsQuery.ToListAsync(ct);
        if (parts.Count == 0) return 0;

        // パーツと装置の紐づきを取得
        var partIds = parts.Select(p => p.Id).ToList();
        var assetComponents = await _context.AssetComponents
            .Include(ac => ac.Asset)
            .Where(ac => partIds.Contains(ac.PartId))
            .ToListAsync(ct);

        var assetLookup = assetComponents
            .GroupBy(ac => ac.PartId)
            .ToDictionary(g => g.Key, g => g.First());

        var points = parts.Select(part =>
        {
            var assetComponent = assetLookup.GetValueOrDefault(part.Id);
            var asset = assetComponent?.Asset;

            var content = BuildPartContent(part);

            return new DesignKnowledgePoint
            {
                Id = part.Id, // パーツIDをポイントIDとして使用
                PartId = part.Id,
                PartNumber = part.PartNumber.Value,
                AssetId = asset?.Id,
                AssetName = asset?.Name,
                ProjectId = null, // Project 概念削除のため
                ProjectName = null,
                Type = "spec",
                Content = content,
                CreatedAt = part.CreatedAt
            };
        }).ToList();

        await _qdrantService.UpsertPartsAsync(points, ct);
        return points.Count;
    }

    /// <summary>
    /// ベクトル検索を行う。
    /// </summary>
    /// <param name="query">検索クエリ</param>
    /// <param name="threshold">類似度閾値</param>
    /// <param name="limit">取得件数上限</param>
    /// <param name="ct">キャンセルトークン</param>
    /// <returns>検索結果</returns>
    public async Task<List<SearchResultDto>> SearchAsync(
        string query,
        double threshold = 0.7,
        int limit = 10,
        CancellationToken ct = default)
    {
        if (_qdrantService == null)
        {
            return new List<SearchResultDto>();
        }

        var results = await _qdrantService.SearchAsync(query, threshold, limit, ct);

        return results.Select(r => new SearchResultDto
        {
            Score = r.Score,
            PartId = r.PartId,
            PartNumber = r.PartNumber,
            AssetName = r.AssetName,
            ProjectName = r.ProjectName,
            Content = r.Content,
            FilePath = r.FilePath
        }).ToList();
    }

    /// <summary>
    /// パーツをベクトルDBから削除する。
    /// </summary>
    public async Task RemoveFromVectorDbAsync(Guid partId, CancellationToken ct = default)
    {
        if (_qdrantService == null) return;
        await _qdrantService.DeleteByPartIdAsync(partId, ct);
    }

    /// <summary>
    /// ベクトルDBをクリアする。
    /// </summary>
    public async Task ClearVectorDbAsync(CancellationToken ct = default)
    {
        if (_qdrantService == null) return;
        await _qdrantService.ClearCollectionAsync(ct);
    }

    /// <summary>
    /// ベクトルDBの統計情報を取得する。
    /// </summary>
    public async Task<VectorDbStats> GetStatsAsync(CancellationToken ct = default)
    {
        if (_qdrantService == null)
        {
            return new VectorDbStats { IsAvailable = false };
        }

        var isConnected = await _qdrantService.CheckConnectionAsync(ct);
        if (!isConnected)
        {
            return new VectorDbStats { IsAvailable = false };
        }

        var collectionExists = await _qdrantService.CollectionExistsAsync(ct);
        var pointCount = collectionExists
            ? await _qdrantService.GetPointCountAsync(ct)
            : 0;

        return new VectorDbStats
        {
            IsAvailable = true,
            CollectionExists = collectionExists,
            PointCount = pointCount
        };
    }

    /// <summary>
    /// パーツ情報から検索用コンテンツを構築する。
    /// </summary>
    private static string BuildPartContent(DesignComponent part)
    {
        var parts = new List<string>
        {
            part.Name,
            part.Type.ToString(),
            part.Memo ?? ""
        };

        // 型別の追加情報
        switch (part)
        {
            case FabricatedPart fab:
                if (!string.IsNullOrEmpty(fab.Material))
                    parts.Add($"材質:{fab.Material}");
                if (!string.IsNullOrEmpty(fab.SurfaceTreatment))
                    parts.Add($"表面処理:{fab.SurfaceTreatment}");
                break;

            case PurchasedPart pur:
                if (!string.IsNullOrEmpty(pur.Manufacturer))
                    parts.Add($"メーカー:{pur.Manufacturer}");
                if (!string.IsNullOrEmpty(pur.ManufacturerPartNumber))
                    parts.Add($"型番:{pur.ManufacturerPartNumber}");
                break;

            case StandardPart std:
                if (!string.IsNullOrEmpty(std.StandardNumber))
                    parts.Add($"規格:{std.StandardNumber}");
                if (!string.IsNullOrEmpty(std.Size))
                    parts.Add($"サイズ:{std.Size}");
                if (!string.IsNullOrEmpty(std.MaterialGrade))
                    parts.Add($"材質等級:{std.MaterialGrade}");
                break;
        }

        return string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
    }
}

/// <summary>
/// 検索結果 DTO。
/// </summary>
public class SearchResultDto
{
    /// <summary>類似度スコア</summary>
    public float Score { get; set; }

    /// <summary>パーツID</summary>
    public Guid PartId { get; set; }

    /// <summary>型式</summary>
    public string PartNumber { get; set; } = string.Empty;

    /// <summary>装置名</summary>
    public string? AssetName { get; set; }

    /// <summary>プロジェクト名</summary>
    public string? ProjectName { get; set; }

    /// <summary>コンテンツ</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>ファイルパス</summary>
    public string? FilePath { get; set; }
}

/// <summary>
/// ベクトルDB統計情報。
/// </summary>
public class VectorDbStats
{
    /// <summary>利用可能か</summary>
    public bool IsAvailable { get; set; }

    /// <summary>コレクションが存在するか</summary>
    public bool CollectionExists { get; set; }

    /// <summary>ポイント数</summary>
    public ulong PointCount { get; set; }
}
