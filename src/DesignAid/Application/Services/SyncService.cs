using Microsoft.EntityFrameworkCore;
using DesignAid.Domain.Entities;
using DesignAid.Domain.ValueObjects;
using DesignAid.Infrastructure.FileSystem;
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

    /// <summary>
    /// 「DB に行があるのに asset.json が無い」装置を洗い出す。
    /// </summary>
    /// <param name="assetsDir">装置ディレクトリの親（`assets/`）</param>
    /// <returns>欠落している装置の一覧。名前順。</returns>
    /// <remarks>
    /// <para>
    /// daid は装置の情報を <c>asset.json</c> と DB の両方に持つが、この 2 つは自動では揃わない。
    /// <c>asset list</c> / <c>asset bom</c> は <c>asset.json</c> しか見ないため、
    /// <b>DB にしか無い装置は存在しないように見える</b>（Issue #1）。
    /// </para>
    /// <para>
    /// <b>ディレクトリ自体が <paramref name="assetsDir"/> に無い装置は対象外</b>にする。
    /// アーカイブ済み（<c>archive/assets/</c> へ移動済み）を欠落と誤報しないためである。
    /// </para>
    /// </remarks>
    public IReadOnlyList<AssetJsonGap> FindAssetJsonGaps(string assetsDir)
    {
        if (string.IsNullOrWhiteSpace(assetsDir) || !Directory.Exists(assetsDir))
            return Array.Empty<AssetJsonGap>();

        var assetJsonReader = new AssetJsonReader();
        var gaps = new List<AssetJsonGap>();

        foreach (var asset in _context.Assets.AsNoTracking().ToList())
        {
            var assetDir = Path.Combine(assetsDir, asset.Name);
            if (!Directory.Exists(assetDir)) continue;
            if (assetJsonReader.Exists(assetDir)) continue;

            gaps.Add(new AssetJsonGap(
                asset.Id,
                asset.Name,
                assetDir,
                asset.DisplayName,
                asset.Description,
                asset.Status,
                asset.Tags,
                asset.CreatedAt));
        }

        return gaps.OrderBy(g => g.Name, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// 欠落した asset.json を DB の内容から復元する。
    /// </summary>
    /// <param name="gaps"><see cref="FindAssetJsonGaps"/> の結果</param>
    /// <param name="onError">1 件ごとの失敗を受け取るコールバック（装置名, 例外）</param>
    /// <returns>復元できた件数</returns>
    /// <remarks>
    /// <b>既存の asset.json は絶対に上書きしない。</b> 書き込む直前にもう一度存在を確認し、
    /// 在れば飛ばす。ファイル側が正本である以上、DB の内容で塗り潰してはならない。
    /// </remarks>
    public int RestoreAssetJson(
        IReadOnlyList<AssetJsonGap> gaps,
        Action<string, Exception>? onError = null)
    {
        var assetJsonReader = new AssetJsonReader();
        var restored = 0;

        foreach (var gap in gaps)
        {
            try
            {
                // 検出から書き込みまでの間に作られていたら触らない
                if (assetJsonReader.Exists(gap.DirectoryPath)) continue;

                assetJsonReader.Write(gap.DirectoryPath, new AssetJson
                {
                    Id = gap.Id,
                    Name = gap.Name,
                    DisplayName = gap.DisplayName,
                    Description = gap.Description,
                    Status = ToJsonStatus(gap.Status),
                    Tags = gap.Tags.Count > 0 ? gap.Tags.ToList() : null,
                    CreatedAt = ToUtc(gap.CreatedAt)
                });
                restored++;
            }
            catch (Exception ex)
            {
                onError?.Invoke(gap.Name, ex);
            }
        }

        return restored;
    }

    /// <summary>
    /// asset.json へ書く日時を UTC に揃える。
    /// </summary>
    /// <remarks>
    /// <c>asset add</c> は <c>DateTime.UtcNow</c> を書くので <c>...Z</c> になるが、
    /// DB から読み直した値は Kind が落ちるため、そのまま書くとローカルオフセット
    /// （<c>+09:00</c> 等）で直列化され、<b>同じ意味の値が 2 通りの表記になる</b>。
    /// 時刻としては等しくても、生成経路によって asset.json の中身が変わるのは避ける。
    /// </remarks>
    public static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        // DB には UTC で入れているので、Kind が落ちていれば UTC とみなす
        DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        _ => value.ToUniversalTime()
    };

    /// <summary>
    /// <see cref="AssetStatus"/> を asset.json の <c>status</c> 文字列へ変換する。
    /// asset.json 側のパース（<c>SyncCommand.TryParseAssetStatus</c>）と<b>対で保つ</b>。
    /// </summary>
    public static string ToJsonStatus(AssetStatus status) => status switch
    {
        AssetStatus.Active => "active",
        AssetStatus.Dormant => "dormant",
        AssetStatus.Archived => "archived",
        AssetStatus.ThirdParty => "third_party",
        _ => "active"
    };
}

/// <summary>
/// DB に行があるのに asset.json が無い装置（Issue #1）。
/// この状態では <c>daid asset list</c> / <c>asset bom</c> から見えない。
/// </summary>
public sealed record AssetJsonGap(
    Guid Id,
    string Name,
    string DirectoryPath,
    string? DisplayName,
    string? Description,
    AssetStatus Status,
    IReadOnlyList<string> Tags,
    DateTime CreatedAt);

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
