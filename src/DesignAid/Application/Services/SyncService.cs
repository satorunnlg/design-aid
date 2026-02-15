using Microsoft.EntityFrameworkCore;
using DesignAid.Domain.Entities;
using DesignAid.Domain.ValueObjects;
using DesignAid.Infrastructure.Persistence;
using DesignAid.Infrastructure.VectorSearch;

namespace DesignAid.Application.Services;

/// <summary>
/// ファイルシステムとデータベースの同期を行うサービス。
/// </summary>
public class SyncService : ISyncService
{
    private readonly DesignAidDbContext _context;
    private readonly HashService _hashService;
    private readonly VectorSearchService? _vectorSearchService;

    /// <summary>
    /// SyncService を初期化する。
    /// </summary>
    public SyncService(
        DesignAidDbContext context,
        HashService hashService,
        VectorSearchService? vectorSearchService = null)
    {
        _context = context;
        _hashService = hashService;
        _vectorSearchService = vectorSearchService;
    }

    /// <summary>
    /// 全パーツを同期する。
    /// </summary>
    /// <param name="force">強制同期（ハッシュを再計算）</param>
    /// <param name="includeVectors">ベクトルDBへの同期も含む</param>
    /// <param name="ct">キャンセルトークン</param>
    /// <returns>同期結果</returns>
    public async Task<SyncResult> SyncAllAsync(
        bool force = false,
        bool includeVectors = false,
        CancellationToken ct = default)
    {
        var result = new SyncResult();
        var parts = await _context.Parts.ToListAsync(ct);

        foreach (var part in parts)
        {
            var partResult = await SyncPartAsync(part, force, ct);
            result.Merge(partResult);
        }

        if (includeVectors && _vectorSearchService != null)
        {
            result.VectorSyncCount = await SyncToVectorIndexAsync(ct);
        }

        return result;
    }

    /// <summary>
    /// 特定のパーツを同期する。
    /// </summary>
    public async Task<SyncResult> SyncPartAsync(
        DesignComponent part,
        bool force = false,
        CancellationToken ct = default)
    {
        var result = new SyncResult();

        if (!Directory.Exists(part.DirectoryPath))
        {
            result.AddError(part.PartNumber.Value, "ディレクトリが存在しません");
            return result;
        }

        // 現在のファイルをスキャン
        var currentFiles = Directory.GetFiles(part.DirectoryPath)
            .Where(f => !Path.GetFileName(f).Equals("part.json", StringComparison.OrdinalIgnoreCase))
            .Select(f => Path.GetRelativePath(part.DirectoryPath, f))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // 登録済みファイル
        var registeredFiles = part.ArtifactHashes.Keys
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // 新規ファイルを検出
        var newFiles = currentFiles.Except(registeredFiles, StringComparer.OrdinalIgnoreCase).ToList();

        // 削除されたファイルを検出
        var deletedFiles = registeredFiles.Except(currentFiles, StringComparer.OrdinalIgnoreCase).ToList();

        // 変更されたファイルを検出
        var modifiedFiles = new List<string>();
        foreach (var (relativePath, expectedHash) in part.ArtifactHashes)
        {
            if (!currentFiles.Contains(relativePath)) continue;

            var fullPath = Path.Combine(part.DirectoryPath, relativePath);
            var currentHash = _hashService.ComputeHash(fullPath);

            if (currentHash.Value != expectedHash.Value)
            {
                modifiedFiles.Add(relativePath);
            }
        }

        var hasChanges = newFiles.Count > 0 || deletedFiles.Count > 0 || modifiedFiles.Count > 0;

        if (!hasChanges && !force)
        {
            return result;
        }

        // ハッシュを更新
        var newHashes = new Dictionary<string, FileHash>();
        foreach (var file in currentFiles)
        {
            var fullPath = Path.Combine(part.DirectoryPath, file);
            var hash = _hashService.ComputeHash(fullPath);
            newHashes[file] = hash;
            part.UpdateArtifactHash(file, hash);
        }

        // 結合ハッシュを更新
        var combinedHash = _hashService.CombineHashes(newHashes.Values);
        part.UpdateCurrentHash(combinedHash.Value);

        await _context.SaveChangesAsync(ct);

        result.AddUpdated(part.PartNumber.Value, new SyncChangeDetail
        {
            NewFiles = newFiles,
            DeletedFiles = deletedFiles,
            ModifiedFiles = modifiedFiles
        });

        return result;
    }

    /// <summary>
    /// ベクトルインデックスにパーツを同期する。
    /// </summary>
    public async Task<int> SyncToVectorIndexAsync(CancellationToken ct = default)
    {
        if (_vectorSearchService == null)
        {
            return 0;
        }

        var parts = await _context.Parts
            .Include(p => p.AssetComponents)
            .ThenInclude(ac => ac.Asset)
            .ToListAsync(ct);

        if (parts.Count == 0)
        {
            return 0;
        }

        var points = parts.Select(part =>
        {
            var assetComponent = part.AssetComponents.FirstOrDefault();
            var asset = assetComponent?.Asset;

            var content = BuildPartContent(part);

            return new DesignKnowledgePoint
            {
                Id = part.Id,
                PartId = part.Id,
                PartNumber = part.PartNumber.Value,
                AssetId = asset?.Id,
                AssetName = asset?.Name,
                ProjectId = null,
                ProjectName = null,
                Type = "spec",
                Content = content,
                CreatedAt = part.CreatedAt
            };
        }).ToList();

        await _vectorSearchService.UpsertPartsAsync(points, ct);

        // HNSW インデックスを再構築
        await _vectorSearchService.RebuildIndexAsync(ct);

        return points.Count;
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
                break;
        }

        return string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
    }
}

/// <summary>
/// 同期結果。
/// </summary>
public class SyncResult
{
    /// <summary>更新されたパーツ</summary>
    public Dictionary<string, SyncChangeDetail> Updated { get; } = new();

    /// <summary>エラーが発生したパーツ</summary>
    public Dictionary<string, string> Errors { get; } = new();

    /// <summary>ベクトルDB同期数</summary>
    public int VectorSyncCount { get; set; }

    /// <summary>更新数</summary>
    public int UpdateCount => Updated.Count;

    /// <summary>エラー数</summary>
    public int ErrorCount => Errors.Count;

    /// <summary>
    /// 更新を追加する。
    /// </summary>
    public void AddUpdated(string partNumber, SyncChangeDetail detail)
    {
        Updated[partNumber] = detail;
    }

    /// <summary>
    /// エラーを追加する。
    /// </summary>
    public void AddError(string partNumber, string message)
    {
        Errors[partNumber] = message;
    }

    /// <summary>
    /// 別の結果をマージする。
    /// </summary>
    public void Merge(SyncResult other)
    {
        foreach (var (key, value) in other.Updated)
        {
            Updated[key] = value;
        }
        foreach (var (key, value) in other.Errors)
        {
            Errors[key] = value;
        }
    }
}

/// <summary>
/// 同期変更詳細。
/// </summary>
public class SyncChangeDetail
{
    /// <summary>新規ファイル</summary>
    public List<string> NewFiles { get; set; } = new();

    /// <summary>削除されたファイル</summary>
    public List<string> DeletedFiles { get; set; } = new();

    /// <summary>変更されたファイル</summary>
    public List<string> ModifiedFiles { get; set; } = new();
}
