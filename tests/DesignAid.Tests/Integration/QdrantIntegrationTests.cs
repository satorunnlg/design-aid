using DesignAid.Infrastructure.Embedding;
using DesignAid.Infrastructure.Qdrant;

namespace DesignAid.Tests.Integration;

/// <summary>
/// Qdrant 統合テスト。
/// 実際の Qdrant インスタンスが必要なテストはスキップ可能。
/// </summary>
[Collection("Qdrant")]
public class QdrantIntegrationTests
{
    private const string TestHost = "localhost";
    private const int TestGrpcPort = 6334;
    private const string TestCollection = "test_design_knowledge";

    /// <summary>
    /// Qdrant が利用可能かチェックする。
    /// </summary>
    private static async Task<bool> IsQdrantAvailableAsync()
    {
        try
        {
            using var client = new Qdrant.Client.QdrantClient(TestHost, TestGrpcPort);
            var collections = await client.ListCollectionsAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }

    [Fact]
    public async Task QdrantService_CreateCollection_SucceedsWhenAvailable()
    {
        // Skip if Qdrant is not available
        if (!await IsQdrantAvailableAsync())
        {
            // Qdrant 未起動時はスキップ
            return;
        }

        // Arrange
        var mockEmbedding = new MockEmbeddingProvider(dimensions: 384);
        using var service = new QdrantService(TestHost, TestGrpcPort, mockEmbedding, TestCollection);

        try
        {
            // Act
            await service.EnsureCollectionAsync();

            // Assert - コレクションが作成されたことを確認
            var exists = await service.CollectionExistsAsync();
            Assert.True(exists);
        }
        finally
        {
            // Cleanup
            try
            {
                using var client = new Qdrant.Client.QdrantClient(TestHost, TestGrpcPort);
                await client.DeleteCollectionAsync(TestCollection);
            }
            catch { /* Ignore cleanup errors */ }
        }
    }

    [Fact]
    public async Task QdrantService_UpsertAndSearch_ReturnsResults()
    {
        // Skip if Qdrant is not available
        if (!await IsQdrantAvailableAsync())
        {
            return;
        }

        // Arrange
        var mockEmbedding = new MockEmbeddingProvider(dimensions: 384);
        using var service = new QdrantService(TestHost, TestGrpcPort, mockEmbedding, TestCollection);

        try
        {
            await service.EnsureCollectionAsync();

            // テストデータをインデックス
            var partId = Guid.NewGuid();
            var point = new DesignKnowledgePoint
            {
                Id = Guid.NewGuid(),
                PartId = partId,
                PartNumber = "TEST-001",
                Type = "spec",
                Content = "SS400 鋼板 昇降機構 ベースプレート",
                CreatedAt = DateTime.UtcNow
            };

            await service.UpsertPartAsync(point);

            // 検索前に少し待機（インデックス反映のため）
            await Task.Delay(500);

            // Act - モックの埋め込みを使用しているため、しきい値を0にして結果を取得
            var results = await service.SearchAsync("鋼板 ベースプレート", threshold: 0.0, limit: 5);

            // Assert
            Assert.NotEmpty(results);
            Assert.Contains(results, r => r.PartId == partId);
        }
        finally
        {
            // Cleanup
            try
            {
                using var client = new Qdrant.Client.QdrantClient(TestHost, TestGrpcPort);
                await client.DeleteCollectionAsync(TestCollection);
            }
            catch { /* Ignore cleanup errors */ }
        }
    }

    [Fact]
    public async Task QdrantService_DeletePoint_RemovesFromIndex()
    {
        // Skip if Qdrant is not available
        if (!await IsQdrantAvailableAsync())
        {
            return;
        }

        // Arrange
        var mockEmbedding = new MockEmbeddingProvider(dimensions: 384);
        using var service = new QdrantService(TestHost, TestGrpcPort, mockEmbedding, TestCollection);

        try
        {
            await service.EnsureCollectionAsync();

            var partId = Guid.NewGuid();
            var point = new DesignKnowledgePoint
            {
                Id = Guid.NewGuid(),
                PartId = partId,
                PartNumber = "DEL-001",
                Type = "spec",
                Content = "テスト削除用",
                CreatedAt = DateTime.UtcNow
            };

            await service.UpsertPartAsync(point);
            await Task.Delay(500);

            // Act
            await service.DeleteByPartIdAsync(partId);
            await Task.Delay(500);

            // Assert
            var results = await service.SearchAsync("削除用", limit: 10, threshold: 0.0);
            Assert.DoesNotContain(results, r => r.PartId == partId);
        }
        finally
        {
            try
            {
                using var client = new Qdrant.Client.QdrantClient(TestHost, TestGrpcPort);
                await client.DeleteCollectionAsync(TestCollection);
            }
            catch { /* Ignore cleanup errors */ }
        }
    }

    [Fact]
    public async Task QdrantService_UpsertParts_BatchInsertWorks()
    {
        // Skip if Qdrant is not available
        if (!await IsQdrantAvailableAsync())
        {
            return;
        }

        // Arrange
        var mockEmbedding = new MockEmbeddingProvider(dimensions: 384);
        using var service = new QdrantService(TestHost, TestGrpcPort, mockEmbedding, TestCollection);

        try
        {
            await service.EnsureCollectionAsync();

            var points = new List<DesignKnowledgePoint>();
            for (int i = 0; i < 5; i++)
            {
                points.Add(new DesignKnowledgePoint
                {
                    Id = Guid.NewGuid(),
                    PartId = Guid.NewGuid(),
                    PartNumber = $"BATCH-{i:D3}",
                    Type = "spec",
                    Content = $"バッチテスト部品 {i}",
                    CreatedAt = DateTime.UtcNow
                });
            }

            // Act
            await service.UpsertPartsAsync(points);
            await Task.Delay(500);

            // Assert
            var count = await service.GetPointCountAsync();
            Assert.True(count >= 5);
        }
        finally
        {
            try
            {
                using var client = new Qdrant.Client.QdrantClient(TestHost, TestGrpcPort);
                await client.DeleteCollectionAsync(TestCollection);
            }
            catch { /* Ignore cleanup errors */ }
        }
    }

    [Fact]
    public async Task MockEmbeddingProvider_GeneratesConsistentEmbeddings()
    {
        // Arrange
        var provider = new MockEmbeddingProvider(dimensions: 384);
        var text = "テストテキスト";

        // Act
        var embedding1 = await provider.GenerateEmbeddingAsync(text);
        var embedding2 = await provider.GenerateEmbeddingAsync(text);

        // Assert - 同じテキストには同じ埋め込みを返す
        Assert.Equal(384, embedding1.Length);
        Assert.Equal(embedding1, embedding2);
    }

    [Fact]
    public async Task MockEmbeddingProvider_GeneratesDifferentEmbeddingsForDifferentTexts()
    {
        // Arrange
        var provider = new MockEmbeddingProvider(dimensions: 384);
        var text1 = "テキスト1";
        var text2 = "テキスト2";

        // Act
        var embedding1 = await provider.GenerateEmbeddingAsync(text1);
        var embedding2 = await provider.GenerateEmbeddingAsync(text2);

        // Assert - 異なるテキストには異なる埋め込みを返す
        Assert.NotEqual(embedding1, embedding2);
    }

    [Fact]
    public async Task MockEmbeddingProvider_BatchGeneration_Works()
    {
        // Arrange
        var provider = new MockEmbeddingProvider(dimensions: 384);
        var texts = new[] { "テキスト1", "テキスト2", "テキスト3" };

        // Act
        var embeddings = await provider.GenerateEmbeddingsAsync(texts);

        // Assert
        Assert.Equal(3, embeddings.Count);
        Assert.All(embeddings, e => Assert.Equal(384, e.Length));
    }
}
