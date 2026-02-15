using Microsoft.EntityFrameworkCore;
using HNSW.Net;
using DesignAid.Infrastructure.Embedding;
using DesignAid.Infrastructure.Persistence;

namespace DesignAid.Infrastructure.VectorSearch;

/// <summary>
/// SQLite + HNSW によるベクトル検索サービス。
/// 外部依存なしでベクトル検索を実現する。
/// </summary>
public class VectorSearchService : IDisposable
{
    private readonly DesignAidDbContext _context;
    private readonly IEmbeddingProvider _embeddingProvider;
    private readonly string _hnswIndexPath;
    private SmallWorld<float[], float>? _hnswIndex;
    private List<(int HnswId, int VectorIndexId)>? _idMapping;
    private bool _disposed;

    /// <summary>
    /// VectorSearchService を初期化する。
    /// </summary>
    /// <param name="context">DB コンテキスト</param>
    /// <param name="embeddingProvider">埋め込みプロバイダー</param>
    /// <param name="hnswIndexPath">HNSW インデックスファイルのパス</param>
    public VectorSearchService(
        DesignAidDbContext context,
        IEmbeddingProvider embeddingProvider,
        string hnswIndexPath)
    {
        _context = context;
        _embeddingProvider = embeddingProvider;
        _hnswIndexPath = hnswIndexPath;
    }

    /// <summary>
    /// パーツ情報をベクトル化して保存する。
    /// </summary>
    /// <param name="point">保存するデータ</param>
    /// <param name="ct">キャンセルトークン</param>
    public async Task UpsertPartAsync(DesignKnowledgePoint point, CancellationToken ct = default)
    {
        var text = BuildSearchableText(point);
        var embedding = await _embeddingProvider.GenerateEmbeddingAsync(text, ct);
        var blob = ToBlob(embedding);

        // 既存エントリを検索
        var existing = await _context.VectorIndex
            .FirstOrDefaultAsync(v => v.PartId == point.PartId.ToString(), ct);

        if (existing != null)
        {
            existing.PartNumber = point.PartNumber;
            existing.Content = BuildContentForStorage(point);
            existing.Embedding = blob;
            existing.Dimensions = embedding.Length;
            existing.AssetId = point.AssetId?.ToString();
            existing.AssetName = point.AssetName;
            existing.ProjectId = point.ProjectId?.ToString();
            existing.ProjectName = point.ProjectName;
            existing.Type = point.Type;
            existing.FilePath = point.FilePath;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            var entry = new VectorIndexEntry
            {
                PartId = point.PartId.ToString(),
                PartNumber = point.PartNumber,
                Content = BuildContentForStorage(point),
                Embedding = blob,
                Dimensions = embedding.Length,
                AssetId = point.AssetId?.ToString(),
                AssetName = point.AssetName,
                ProjectId = point.ProjectId?.ToString(),
                ProjectName = point.ProjectName,
                Type = point.Type,
                FilePath = point.FilePath,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _context.VectorIndex.Add(entry);
        }

        await _context.SaveChangesAsync(ct);

        // HNSW インデックスを無効化（次回検索時に再構築）
        InvalidateIndex();
    }

    /// <summary>
    /// 複数のパーツ情報を一括で保存する。
    /// </summary>
    public async Task UpsertPartsAsync(
        IEnumerable<DesignKnowledgePoint> points,
        CancellationToken ct = default)
    {
        var pointsList = points.ToList();
        if (pointsList.Count == 0) return;

        var texts = pointsList.Select(BuildSearchableText).ToList();
        var embeddings = await _embeddingProvider.GenerateEmbeddingsAsync(texts, ct);

        for (int i = 0; i < pointsList.Count; i++)
        {
            var point = pointsList[i];
            var embedding = embeddings[i];
            var blob = ToBlob(embedding);

            var existing = await _context.VectorIndex
                .FirstOrDefaultAsync(v => v.PartId == point.PartId.ToString(), ct);

            if (existing != null)
            {
                existing.PartNumber = point.PartNumber;
                existing.Content = BuildContentForStorage(point);
                existing.Embedding = blob;
                existing.Dimensions = embedding.Length;
                existing.AssetId = point.AssetId?.ToString();
                existing.AssetName = point.AssetName;
                existing.ProjectId = point.ProjectId?.ToString();
                existing.ProjectName = point.ProjectName;
                existing.Type = point.Type;
                existing.FilePath = point.FilePath;
                existing.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                var entry = new VectorIndexEntry
                {
                    PartId = point.PartId.ToString(),
                    PartNumber = point.PartNumber,
                    Content = BuildContentForStorage(point),
                    Embedding = blob,
                    Dimensions = embedding.Length,
                    AssetId = point.AssetId?.ToString(),
                    AssetName = point.AssetName,
                    ProjectId = point.ProjectId?.ToString(),
                    ProjectName = point.ProjectName,
                    Type = point.Type,
                    FilePath = point.FilePath,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _context.VectorIndex.Add(entry);
            }
        }

        await _context.SaveChangesAsync(ct);
        InvalidateIndex();
    }

    /// <summary>
    /// テキストで類似検索を行う。
    /// </summary>
    /// <param name="query">検索クエリ</param>
    /// <param name="threshold">類似度閾値（0.0-1.0）</param>
    /// <param name="limit">取得件数上限</param>
    /// <param name="ct">キャンセルトークン</param>
    /// <returns>検索結果リスト</returns>
    public async Task<List<SearchResult>> SearchAsync(
        string query,
        double threshold = 0.7,
        int limit = 10,
        CancellationToken ct = default)
    {
        var count = await _context.VectorIndex.CountAsync(ct);
        if (count == 0)
        {
            return new List<SearchResult>();
        }

        // HNSW インデックスを構築（未構築の場合）
        await EnsureIndexLoadedAsync(ct);

        if (_hnswIndex == null || _idMapping == null)
        {
            return new List<SearchResult>();
        }

        var queryEmbedding = await _embeddingProvider.GenerateEmbeddingAsync(query, ct);

        // KNN 検索を実行
        var knnResults = _hnswIndex.KNNSearch(queryEmbedding, limit);

        var results = new List<SearchResult>();

        foreach (var knnResult in knnResults)
        {
            // 距離→類似度に変換（コサイン距離: score = 1.0 - distance）
            var score = 1.0f - knnResult.Distance;

            if (score < threshold) continue;

            // HNSW 内部ID → VectorIndex ID のマッピング
            var mapping = _idMapping.FirstOrDefault(m => m.HnswId == knnResult.Id);
            if (mapping == default) continue;

            var entry = await _context.VectorIndex
                .FirstOrDefaultAsync(v => v.Id == mapping.VectorIndexId, ct);

            if (entry == null) continue;

            results.Add(new SearchResult
            {
                Id = Guid.TryParse(entry.PartId, out var pid) ? pid : Guid.Empty,
                Score = score,
                PartId = Guid.TryParse(entry.PartId, out var pid2) ? pid2 : Guid.Empty,
                PartNumber = entry.PartNumber,
                AssetId = string.IsNullOrEmpty(entry.AssetId) ? null : (Guid.TryParse(entry.AssetId, out var aid) ? aid : null),
                AssetName = entry.AssetName,
                ProjectId = string.IsNullOrEmpty(entry.ProjectId) ? null : (Guid.TryParse(entry.ProjectId, out var prid) ? prid : null),
                ProjectName = entry.ProjectName,
                Type = entry.Type ?? "spec",
                Content = entry.Content,
                FilePath = entry.FilePath
            });
        }

        return results.OrderByDescending(r => r.Score).ToList();
    }

    /// <summary>
    /// 特定のパーツIDに関連するエントリを削除する。
    /// </summary>
    public async Task DeleteByPartIdAsync(Guid partId, CancellationToken ct = default)
    {
        var entries = await _context.VectorIndex
            .Where(v => v.PartId == partId.ToString())
            .ToListAsync(ct);

        if (entries.Count > 0)
        {
            _context.VectorIndex.RemoveRange(entries);
            await _context.SaveChangesAsync(ct);
            InvalidateIndex();
        }
    }

    /// <summary>
    /// HNSW インデックスを SQLite のデータから完全再構築する。
    /// </summary>
    public async Task RebuildIndexAsync(CancellationToken ct = default)
    {
        var entries = await _context.VectorIndex.ToListAsync(ct);

        if (entries.Count == 0)
        {
            _hnswIndex = null;
            _idMapping = null;
            DeleteIndexFile();
            return;
        }

        // ベクトルを復元
        var vectors = entries.Select(e => FromBlob(e.Embedding)).ToList();

        // HNSW インデックスを構築
        var parameters = new SmallWorldParameters()
        {
            M = 16,
            LevelLambda = 1.0 / Math.Log(16)
        };

        _hnswIndex = new SmallWorld<float[], float>(
            CosineDistance.SIMDForUnits,
            DefaultRandomGenerator.Instance,
            parameters,
            threadSafe: true);

        var hnswIds = _hnswIndex.AddItems(vectors);

        // ID マッピングを構築
        _idMapping = new List<(int HnswId, int VectorIndexId)>();
        for (int i = 0; i < entries.Count; i++)
        {
            _idMapping.Add((hnswIds[i], entries[i].Id));
        }

        // グラフをファイルに保存
        await SerializeIndexAsync(vectors);
    }

    /// <summary>
    /// ベクトルインデックス内のエントリ数を取得する。
    /// </summary>
    public async Task<long> GetPointCountAsync(CancellationToken ct = default)
    {
        return await _context.VectorIndex.CountAsync(ct);
    }

    /// <summary>
    /// ベクトルインデックスが利用可能かどうかを確認する。
    /// </summary>
    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        var count = await _context.VectorIndex.CountAsync(ct);
        return count > 0;
    }

    /// <summary>
    /// ベクトルインデックスをクリアする。
    /// </summary>
    public async Task ClearAsync(CancellationToken ct = default)
    {
        var entries = await _context.VectorIndex.ToListAsync(ct);
        if (entries.Count > 0)
        {
            _context.VectorIndex.RemoveRange(entries);
            await _context.SaveChangesAsync(ct);
        }

        _hnswIndex = null;
        _idMapping = null;
        DeleteIndexFile();
    }

    /// <summary>
    /// HNSW インデックスをメモリに読み込む（遅延ロード）。
    /// </summary>
    private async Task EnsureIndexLoadedAsync(CancellationToken ct = default)
    {
        if (_hnswIndex != null && _idMapping != null)
        {
            return;
        }

        var entries = await _context.VectorIndex.ToListAsync(ct);
        if (entries.Count == 0)
        {
            return;
        }

        var vectors = entries.Select(e => FromBlob(e.Embedding)).ToList();

        // キャッシュファイルからの復元を試みる
        if (File.Exists(_hnswIndexPath))
        {
            try
            {
                using var stream = File.OpenRead(_hnswIndexPath);
                var deserialized = SmallWorld<float[], float>.DeserializeGraph(
                    vectors,
                    CosineDistance.SIMDForUnits,
                    DefaultRandomGenerator.Instance,
                    stream,
                    threadSafe: true);
                _hnswIndex = deserialized.Graph;

                // ID マッピングを構築
                _idMapping = new List<(int HnswId, int VectorIndexId)>();
                for (int i = 0; i < entries.Count; i++)
                {
                    _idMapping.Add((i, entries[i].Id));
                }
                return;
            }
            catch
            {
                // キャッシュからの復元に失敗した場合は再構築
            }
        }

        // インデックスを新規構築
        await RebuildIndexAsync(ct);
    }

    /// <summary>
    /// HNSW グラフをファイルにシリアライズする。
    /// </summary>
    private async Task SerializeIndexAsync(List<float[]> vectors)
    {
        if (_hnswIndex == null) return;

        try
        {
            var directory = Path.GetDirectoryName(_hnswIndexPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var stream = File.Create(_hnswIndexPath);
            _hnswIndex.SerializeGraph(stream);
            await stream.FlushAsync();
        }
        catch
        {
            // シリアライズ失敗は致命的ではない（次回再構築する）
        }
    }

    /// <summary>
    /// HNSW インデックスキャッシュを無効化する。
    /// </summary>
    private void InvalidateIndex()
    {
        _hnswIndex = null;
        _idMapping = null;
        DeleteIndexFile();
    }

    /// <summary>
    /// HNSW インデックスファイルを削除する。
    /// </summary>
    private void DeleteIndexFile()
    {
        try
        {
            if (File.Exists(_hnswIndexPath))
            {
                File.Delete(_hnswIndexPath);
            }
        }
        catch
        {
            // 削除失敗は無視
        }
    }

    /// <summary>
    /// 検索可能なテキストを構築する。
    /// </summary>
    private static string BuildSearchableText(DesignKnowledgePoint point)
    {
        var parts = new List<string>
        {
            point.PartNumber,
            point.Content
        };

        if (!string.IsNullOrEmpty(point.AssetName))
            parts.Add(point.AssetName);

        if (!string.IsNullOrEmpty(point.ProjectName))
            parts.Add(point.ProjectName);

        return string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
    }

    /// <summary>
    /// 保存用のコンテンツ文字列を構築する。
    /// </summary>
    private static string BuildContentForStorage(DesignKnowledgePoint point)
    {
        return point.Content;
    }

    /// <summary>
    /// float 配列を BLOB（byte 配列）に変換する。
    /// </summary>
    public static byte[] ToBlob(float[] vector)
    {
        var bytes = new byte[vector.Length * sizeof(float)];
        Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    /// <summary>
    /// BLOB（byte 配列）を float 配列に変換する。
    /// </summary>
    public static float[] FromBlob(byte[] blob)
    {
        var vector = new float[blob.Length / sizeof(float)];
        Buffer.BlockCopy(blob, 0, vector, 0, blob.Length);
        return vector;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!_disposed)
        {
            _hnswIndex = null;
            _idMapping = null;
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// ベクトルインデックスに保存するパーツ知識データ。
/// </summary>
public class DesignKnowledgePoint
{
    /// <summary>ポイントID（識別子）</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>パーツ内部ID</summary>
    public Guid PartId { get; set; }

    /// <summary>型式</summary>
    public string PartNumber { get; set; } = string.Empty;

    /// <summary>装置内部ID</summary>
    public Guid? AssetId { get; set; }

    /// <summary>装置名</summary>
    public string? AssetName { get; set; }

    /// <summary>プロジェクト内部ID</summary>
    public Guid? ProjectId { get; set; }

    /// <summary>プロジェクト名</summary>
    public string? ProjectName { get; set; }

    /// <summary>データ種別（spec/memo/parameter）</summary>
    public string Type { get; set; } = "spec";

    /// <summary>検索対象となるコンテンツ</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>関連ファイルパス</summary>
    public string? FilePath { get; set; }

    /// <summary>作成日時</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 検索結果。
/// </summary>
public class SearchResult
{
    /// <summary>ポイントID</summary>
    public Guid Id { get; set; }

    /// <summary>類似度スコア（0.0-1.0）</summary>
    public float Score { get; set; }

    /// <summary>パーツ内部ID</summary>
    public Guid PartId { get; set; }

    /// <summary>型式</summary>
    public string PartNumber { get; set; } = string.Empty;

    /// <summary>装置内部ID</summary>
    public Guid? AssetId { get; set; }

    /// <summary>装置名</summary>
    public string? AssetName { get; set; }

    /// <summary>プロジェクト内部ID</summary>
    public Guid? ProjectId { get; set; }

    /// <summary>プロジェクト名</summary>
    public string? ProjectName { get; set; }

    /// <summary>データ種別</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>コンテンツ</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>関連ファイルパス</summary>
    public string? FilePath { get; set; }
}

/// <summary>
/// ベクトルインデックスのエントリ（SQLite VectorIndex テーブル）。
/// </summary>
public class VectorIndexEntry
{
    /// <summary>主キー（自動採番）</summary>
    public int Id { get; set; }

    /// <summary>パーツID（Parts テーブルの Id）</summary>
    public string PartId { get; set; } = string.Empty;

    /// <summary>型式</summary>
    public string PartNumber { get; set; } = string.Empty;

    /// <summary>検索対象コンテンツ</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>埋め込みベクトル（BLOB）</summary>
    public byte[] Embedding { get; set; } = Array.Empty<byte>();

    /// <summary>ベクトル次元数</summary>
    public int Dimensions { get; set; }

    /// <summary>装置ID</summary>
    public string? AssetId { get; set; }

    /// <summary>装置名</summary>
    public string? AssetName { get; set; }

    /// <summary>プロジェクトID</summary>
    public string? ProjectId { get; set; }

    /// <summary>プロジェクト名</summary>
    public string? ProjectName { get; set; }

    /// <summary>データ種別</summary>
    public string? Type { get; set; }

    /// <summary>関連ファイルパス</summary>
    public string? FilePath { get; set; }

    /// <summary>作成日時</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>更新日時</summary>
    public DateTime UpdatedAt { get; set; }
}
