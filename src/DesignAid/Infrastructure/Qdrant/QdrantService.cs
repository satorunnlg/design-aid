using Qdrant.Client;
using Qdrant.Client.Grpc;
using DesignAid.Infrastructure.Embedding;

namespace DesignAid.Infrastructure.Qdrant;

/// <summary>
/// Qdrant ベクトルデータベースとの連携を行うサービス。
/// </summary>
public class QdrantService : IDisposable
{
    private readonly QdrantClient _client;
    private readonly IEmbeddingProvider _embeddingProvider;
    private readonly string _collectionName;
    private bool _disposed;

    /// <summary>
    /// QdrantService を初期化する。
    /// </summary>
    /// <param name="host">Qdrant ホスト</param>
    /// <param name="port">Qdrant ポート</param>
    /// <param name="embeddingProvider">埋め込みプロバイダー</param>
    /// <param name="collectionName">コレクション名</param>
    public QdrantService(
        string host,
        int port,
        IEmbeddingProvider embeddingProvider,
        string collectionName = "design_knowledge")
    {
        _client = new QdrantClient(host, port);
        _embeddingProvider = embeddingProvider;
        _collectionName = collectionName;
    }

    /// <summary>
    /// Qdrant への接続を確認する。
    /// </summary>
    /// <returns>接続成功時は true</returns>
    public async Task<bool> CheckConnectionAsync(CancellationToken ct = default)
    {
        try
        {
            await _client.ListCollectionsAsync(ct);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// コレクションが存在するか確認する。
    /// </summary>
    public async Task<bool> CollectionExistsAsync(CancellationToken ct = default)
    {
        try
        {
            var collections = await _client.ListCollectionsAsync(ct);
            return collections.Any(c => c == _collectionName);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// コレクションを作成する（存在しない場合のみ）。
    /// </summary>
    public async Task EnsureCollectionAsync(CancellationToken ct = default)
    {
        if (await CollectionExistsAsync(ct))
        {
            return;
        }

        await _client.CreateCollectionAsync(
            _collectionName,
            new VectorParams
            {
                Size = (ulong)_embeddingProvider.Dimensions,
                Distance = Distance.Cosine
            },
            cancellationToken: ct);
    }

    /// <summary>
    /// コレクション内のポイント数を取得する。
    /// </summary>
    public async Task<ulong> GetPointCountAsync(CancellationToken ct = default)
    {
        try
        {
            var info = await _client.GetCollectionInfoAsync(_collectionName, ct);
            return info.PointsCount;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// パーツ情報をベクトル化して保存する。
    /// </summary>
    /// <param name="point">保存するデータ</param>
    /// <param name="ct">キャンセルトークン</param>
    public async Task UpsertPartAsync(DesignKnowledgePoint point, CancellationToken ct = default)
    {
        await EnsureCollectionAsync(ct);

        // テキストを連結してベクトル化
        var text = BuildSearchableText(point);
        var embedding = await _embeddingProvider.GenerateEmbeddingAsync(text, ct);

        var qdrantPoint = new PointStruct
        {
            Id = new PointId { Uuid = point.Id.ToString() },
            Vectors = embedding,
            Payload =
            {
                ["part_id"] = point.PartId.ToString(),
                ["part_number"] = point.PartNumber,
                ["asset_id"] = point.AssetId?.ToString() ?? "",
                ["asset_name"] = point.AssetName ?? "",
                ["project_id"] = point.ProjectId?.ToString() ?? "",
                ["project_name"] = point.ProjectName ?? "",
                ["type"] = point.Type,
                ["content"] = point.Content,
                ["file_path"] = point.FilePath ?? "",
                ["created_at"] = point.CreatedAt.ToString("o")
            }
        };

        await _client.UpsertAsync(_collectionName, new[] { qdrantPoint }, cancellationToken: ct);
    }

    /// <summary>
    /// 複数のパーツ情報を一括で保存する。
    /// </summary>
    public async Task UpsertPartsAsync(
        IEnumerable<DesignKnowledgePoint> points,
        CancellationToken ct = default)
    {
        await EnsureCollectionAsync(ct);

        var pointsList = points.ToList();
        if (pointsList.Count == 0) return;

        // テキストを連結してベクトル化
        var texts = pointsList.Select(BuildSearchableText).ToList();
        var embeddings = await _embeddingProvider.GenerateEmbeddingsAsync(texts, ct);

        var qdrantPoints = pointsList.Zip(embeddings, (point, embedding) =>
        {
            return new PointStruct
            {
                Id = new PointId { Uuid = point.Id.ToString() },
                Vectors = embedding,
                Payload =
                {
                    ["part_id"] = point.PartId.ToString(),
                    ["part_number"] = point.PartNumber,
                    ["asset_id"] = point.AssetId?.ToString() ?? "",
                    ["asset_name"] = point.AssetName ?? "",
                    ["project_id"] = point.ProjectId?.ToString() ?? "",
                    ["project_name"] = point.ProjectName ?? "",
                    ["type"] = point.Type,
                    ["content"] = point.Content,
                    ["file_path"] = point.FilePath ?? "",
                    ["created_at"] = point.CreatedAt.ToString("o")
                }
            };
        }).ToList();

        await _client.UpsertAsync(_collectionName, qdrantPoints, cancellationToken: ct);
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
        if (!await CollectionExistsAsync(ct))
        {
            return new List<SearchResult>();
        }

        var queryEmbedding = await _embeddingProvider.GenerateEmbeddingAsync(query, ct);

        var searchResult = await _client.SearchAsync(
            _collectionName,
            queryEmbedding,
            limit: (ulong)limit,
            scoreThreshold: (float)threshold,
            cancellationToken: ct);

        return searchResult.Select(r => new SearchResult
        {
            Id = Guid.Parse(r.Id.Uuid),
            Score = r.Score,
            PartId = GetPayloadGuid(r, "part_id"),
            PartNumber = GetPayloadString(r, "part_number"),
            AssetId = GetPayloadNullableGuid(r, "asset_id"),
            AssetName = GetPayloadString(r, "asset_name"),
            ProjectId = GetPayloadNullableGuid(r, "project_id"),
            ProjectName = GetPayloadString(r, "project_name"),
            Type = GetPayloadString(r, "type"),
            Content = GetPayloadString(r, "content"),
            FilePath = GetPayloadString(r, "file_path")
        }).ToList();
    }

    /// <summary>
    /// 特定のパーツIDに関連するポイントを削除する。
    /// </summary>
    public async Task DeleteByPartIdAsync(Guid partId, CancellationToken ct = default)
    {
        if (!await CollectionExistsAsync(ct))
        {
            return;
        }

        await _client.DeleteAsync(
            _collectionName,
            new Filter
            {
                Must =
                {
                    new Condition
                    {
                        Field = new FieldCondition
                        {
                            Key = "part_id",
                            Match = new Match { Keyword = partId.ToString() }
                        }
                    }
                }
            },
            cancellationToken: ct);
    }

    /// <summary>
    /// コレクション内の全ポイントを削除する。
    /// </summary>
    public async Task ClearCollectionAsync(CancellationToken ct = default)
    {
        if (await CollectionExistsAsync(ct))
        {
            await _client.DeleteCollectionAsync(_collectionName);
        }
        await EnsureCollectionAsync(ct);
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

    private static string GetPayloadString(ScoredPoint point, string key)
    {
        if (point.Payload.TryGetValue(key, out var value))
        {
            return value.StringValue ?? string.Empty;
        }
        return string.Empty;
    }

    private static Guid GetPayloadGuid(ScoredPoint point, string key)
    {
        var str = GetPayloadString(point, key);
        return Guid.TryParse(str, out var guid) ? guid : Guid.Empty;
    }

    private static Guid? GetPayloadNullableGuid(ScoredPoint point, string key)
    {
        var str = GetPayloadString(point, key);
        if (string.IsNullOrEmpty(str)) return null;
        return Guid.TryParse(str, out var guid) ? guid : null;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!_disposed)
        {
            _client.Dispose();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Qdrant に保存するパーツ知識データ。
/// </summary>
public class DesignKnowledgePoint
{
    /// <summary>ポイントID（Qdrant内での識別子）</summary>
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
