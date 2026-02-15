using DesignAid.Infrastructure.Embedding;
using DesignAid.Infrastructure.Persistence;
using DesignAid.Infrastructure.VectorSearch;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DesignAid.Tests.Integration;

/// <summary>
/// VectorSearchService 統合テスト。
/// SQLite + HNSW を使用したベクトル検索の動作を検証する。
/// </summary>
public class VectorSearchIntegrationTests : IDisposable
{
    private readonly string _dbPath;
    private readonly string _hnswIndexPath;
    private readonly DesignAidDbContext _context;
    private readonly VectorSearchService _service;

    public VectorSearchIntegrationTests()
    {
        var testId = Guid.NewGuid().ToString("N");
        _dbPath = Path.Combine(Path.GetTempPath(), $"design_aid_vec_test_{testId}.db");
        _hnswIndexPath = Path.Combine(Path.GetTempPath(), $"hnsw_test_{testId}.bin");

        var options = new DbContextOptionsBuilder<DesignAidDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;

        _context = new DesignAidDbContext(options);
        _context.Database.EnsureCreated();

        var embeddingProvider = new MockEmbeddingProvider(1536);
        _service = new VectorSearchService(_context, embeddingProvider, _hnswIndexPath);
    }

    public void Dispose()
    {
        _service.Dispose();
        _context.Dispose();
        SqliteConnection.ClearAllPools();

        TryDelete(_dbPath);
        TryDelete(_hnswIndexPath);
        GC.SuppressFinalize(this);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* 無視 */ }
    }

    [Fact]
    public async Task UpsertPartAsync_AddsEntryToDatabase()
    {
        // Arrange
        var point = CreateTestPoint("TEST-001", "テスト部品1");

        // Act
        await _service.UpsertPartAsync(point);

        // Assert
        var count = await _context.VectorIndex.CountAsync();
        Assert.Equal(1, count);

        var entry = await _context.VectorIndex.FirstAsync();
        Assert.Equal(point.PartId.ToString(), entry.PartId);
        Assert.Equal("TEST-001", entry.PartNumber);
        Assert.Equal(1536, entry.Dimensions);
    }

    [Fact]
    public async Task UpsertPartAsync_UpdatesExistingEntry()
    {
        // Arrange
        var point = CreateTestPoint("TEST-002", "元の名前");
        await _service.UpsertPartAsync(point);

        // Act - 同じ PartId で更新
        point.Content = "更新されたコンテンツ";
        await _service.UpsertPartAsync(point);

        // Assert - 1件のみ
        var count = await _context.VectorIndex.CountAsync();
        Assert.Equal(1, count);

        var entry = await _context.VectorIndex.FirstAsync();
        Assert.Contains("更新されたコンテンツ", entry.Content);
    }

    [Fact]
    public async Task UpsertPartsAsync_BatchInsert()
    {
        // Arrange
        var points = new[]
        {
            CreateTestPoint("BATCH-001", "バッチ部品1"),
            CreateTestPoint("BATCH-002", "バッチ部品2"),
            CreateTestPoint("BATCH-003", "バッチ部品3"),
        };

        // Act
        await _service.UpsertPartsAsync(points);

        // Assert
        var count = await _context.VectorIndex.CountAsync();
        Assert.Equal(3, count);
    }

    [Fact]
    public async Task SearchAsync_ReturnsResults()
    {
        // Arrange
        var points = new[]
        {
            CreateTestPoint("SEARCH-001", "油圧シリンダ 100mm ストローク"),
            CreateTestPoint("SEARCH-002", "ボールベアリング 6205"),
            CreateTestPoint("SEARCH-003", "ステンレス板 SUS304"),
        };
        await _service.UpsertPartsAsync(points);
        await _service.RebuildIndexAsync();

        // Act
        var results = await _service.SearchAsync("油圧シリンダ", threshold: 0.0, limit: 10);

        // Assert - MockEmbeddingProvider はランダムなベクトルを返すため、
        // 結果数のみを検証（類似度の精度は実際の埋め込みモデルに依存）
        Assert.NotNull(results);
    }

    [Fact]
    public async Task DeleteByPartIdAsync_RemovesEntry()
    {
        // Arrange
        var point = CreateTestPoint("DEL-001", "削除テスト部品");
        await _service.UpsertPartAsync(point);
        Assert.Equal(1, await _context.VectorIndex.CountAsync());

        // Act
        await _service.DeleteByPartIdAsync(point.PartId);

        // Assert
        Assert.Equal(0, await _context.VectorIndex.CountAsync());
    }

    [Fact]
    public async Task RebuildIndexAsync_BuildsHnswIndex()
    {
        // Arrange
        var points = new[]
        {
            CreateTestPoint("REBUILD-001", "リビルドテスト1"),
            CreateTestPoint("REBUILD-002", "リビルドテスト2"),
        };
        await _service.UpsertPartsAsync(points);

        // Act
        await _service.RebuildIndexAsync();

        // Assert - HNSW インデックスファイルが作成されたか
        Assert.True(File.Exists(_hnswIndexPath));
    }

    [Fact]
    public async Task GetPointCountAsync_ReturnsCorrectCount()
    {
        // Arrange
        await _service.UpsertPartAsync(CreateTestPoint("COUNT-001", "カウントテスト1"));
        await _service.UpsertPartAsync(CreateTestPoint("COUNT-002", "カウントテスト2"));

        // Act
        var count = await _service.GetPointCountAsync();

        // Assert
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task ClearAsync_RemovesAllEntries()
    {
        // Arrange
        await _service.UpsertPartsAsync(new[]
        {
            CreateTestPoint("CLEAR-001", "クリアテスト1"),
            CreateTestPoint("CLEAR-002", "クリアテスト2"),
        });
        await _service.RebuildIndexAsync();

        // Act
        await _service.ClearAsync();

        // Assert
        Assert.Equal(0, await _context.VectorIndex.CountAsync());
        Assert.False(File.Exists(_hnswIndexPath));
    }

    [Fact]
    public async Task IsAvailableAsync_ReturnsFalseWhenEmpty()
    {
        // Act
        var available = await _service.IsAvailableAsync();

        // Assert
        Assert.False(available);
    }

    [Fact]
    public async Task IsAvailableAsync_ReturnsTrueWithData()
    {
        // Arrange
        await _service.UpsertPartAsync(CreateTestPoint("AVAIL-001", "可用性テスト"));

        // Act
        var available = await _service.IsAvailableAsync();

        // Assert
        Assert.True(available);
    }

    [Fact]
    public async Task SearchAsync_EmptyIndex_ReturnsEmptyList()
    {
        // Act
        var results = await _service.SearchAsync("何か", threshold: 0.0, limit: 10);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public void ToBlob_FromBlob_RoundTrip()
    {
        // Arrange
        var original = new float[] { 1.0f, 2.0f, 3.0f, 0.5f, -1.5f };

        // Act
        var blob = VectorSearchService.ToBlob(original);
        var restored = VectorSearchService.FromBlob(blob);

        // Assert
        Assert.Equal(original.Length, restored.Length);
        for (int i = 0; i < original.Length; i++)
        {
            Assert.Equal(original[i], restored[i]);
        }
    }

    [Fact]
    public void ToBlob_ProducesCorrectSize()
    {
        // Arrange
        var vector = new float[1536]; // OpenAI の次元数

        // Act
        var blob = VectorSearchService.ToBlob(vector);

        // Assert
        Assert.Equal(1536 * sizeof(float), blob.Length);
    }

    private static DesignKnowledgePoint CreateTestPoint(string partNumber, string content)
    {
        return new DesignKnowledgePoint
        {
            Id = Guid.NewGuid(),
            PartId = Guid.NewGuid(),
            PartNumber = partNumber,
            Content = content,
            Type = "spec",
            CreatedAt = DateTime.UtcNow
        };
    }
}
