using DesignAid.Application.Services;
using DesignAid.Dashboard.Services;
using DesignAid.Domain.Entities;
using DesignAid.Domain.ValueObjects;
using DesignAid.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DesignAid.Tests.Dashboard;

/// <summary>
/// DashboardService のテスト。
/// </summary>
public class DashboardServiceTests : IDisposable
{
    private readonly DesignAidDbContext _context;
    private readonly IDbContextFactory<DesignAidDbContext> _contextFactory;
    private readonly DashboardService _service;
    private readonly string _tempDir;

    public DashboardServiceTests()
    {
        var options = new DbContextOptionsBuilder<DesignAidDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new DesignAidDbContext(options);
        _contextFactory = new TestDbContextFactory(options);
        _service = new DashboardService(_contextFactory);
        _tempDir = Path.Combine(Path.GetTempPath(), $"da-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        _context.Dispose();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task GetSummaryAsync_EmptyDatabase_ReturnsZeroCounts()
    {
        // Act
        var summary = await _service.GetSummaryAsync();

        // Assert
        Assert.Equal(0, summary.AssetCount);
        Assert.Equal(0, summary.PartCount);
        Assert.Equal(0, summary.TypeSummary.Fabricated);
        Assert.Equal(0, summary.StatusSummary.Draft);
        Assert.Empty(summary.RecentParts);
    }

    [Fact]
    public async Task GetSummaryAsync_WithData_ReturnsCorrectCounts()
    {
        // Arrange
        await SeedTestData();

        // Act
        var summary = await _service.GetSummaryAsync();

        // Assert
        Assert.Equal(1, summary.AssetCount);
        Assert.Equal(3, summary.PartCount);
        Assert.Equal(1, summary.TypeSummary.Fabricated);
        Assert.Equal(1, summary.TypeSummary.Purchased);
        Assert.Equal(1, summary.TypeSummary.Standard);
        Assert.Equal(3, summary.StatusSummary.Draft);
    }

    [Fact]
    public async Task GetSummaryAsync_RecentParts_ReturnsUpToFive()
    {
        // Arrange
        await SeedTestData();

        // Act
        var summary = await _service.GetSummaryAsync();

        // Assert
        Assert.Equal(3, summary.RecentParts.Count);
        // 更新日時の降順で返ること
        for (int i = 0; i < summary.RecentParts.Count - 1; i++)
        {
            Assert.True(summary.RecentParts[i].UpdatedAt >= summary.RecentParts[i + 1].UpdatedAt);
        }
    }

    [Fact]
    public async Task GetPartsAsync_NoFilter_ReturnsAll()
    {
        // Arrange
        await SeedTestData();

        // Act
        var parts = await _service.GetPartsAsync();

        // Assert
        Assert.Equal(3, parts.Count);
    }

    [Fact]
    public async Task GetPartsAsync_TypeFilter_ReturnsFiltered()
    {
        // Arrange
        await SeedTestData();

        // Act
        var parts = await _service.GetPartsAsync(typeFilter: PartType.Fabricated);

        // Assert
        Assert.Single(parts);
        Assert.Equal("製作物", parts[0].Type);
    }

    [Fact]
    public async Task GetPartsAsync_SearchText_ReturnsMatched()
    {
        // Arrange
        await SeedTestData();

        // Act
        var parts = await _service.GetPartsAsync(searchText: "BOLT");

        // Assert
        Assert.Single(parts);
        Assert.Equal("BOLT-M10", parts[0].PartNumber);
    }

    [Fact]
    public async Task GetAssetsAsync_ReturnsAllAssets()
    {
        // Arrange
        await SeedTestData();

        // Act
        var assets = await _service.GetAssetsAsync();

        // Assert
        Assert.Single(assets);
        Assert.Equal("test-asset", assets[0].Name);
    }

    [Fact]
    public async Task GetAssetDetailAsync_ValidId_ReturnsDetail()
    {
        // Arrange
        var (assetId, _) = await SeedTestData();

        // Act
        var detail = await _service.GetAssetDetailAsync(assetId);

        // Assert
        Assert.NotNull(detail);
        Assert.Equal("test-asset", detail.Name);
        Assert.Equal(2, detail.Parts.Count);
    }

    [Fact]
    public async Task GetAssetDetailAsync_InvalidId_ReturnsNull()
    {
        // Act
        var detail = await _service.GetAssetDetailAsync(Guid.NewGuid());

        // Assert
        Assert.Null(detail);
    }

    [Fact]
    public async Task RunIntegrityCheckAsync_EmptyDatabase_ReturnsEmpty()
    {
        // Act
        var results = await _service.RunIntegrityCheckAsync();

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public async Task RunIntegrityCheckAsync_WithParts_ReturnsResults()
    {
        // Arrange
        await SeedTestData();

        // Act
        var results = await _service.RunIntegrityCheckAsync();

        // Assert
        Assert.Equal(3, results.Count);
        // ディレクトリが存在するパーツは OK、存在しないパーツは Error
        Assert.Contains(results, r => r.CheckStatus == "Ok");
    }

    /// <summary>
    /// テストデータを投入する。
    /// </summary>
    private async Task<(Guid assetId, List<Guid> partIds)> SeedTestData()
    {
        // パーツディレクトリ作成
        var partDir1 = Path.Combine(_tempDir, "PLATE-001");
        var partDir2 = Path.Combine(_tempDir, "BOLT-M10");
        var partDir3 = Path.Combine(_tempDir, "MOTOR-01");
        Directory.CreateDirectory(partDir1);
        Directory.CreateDirectory(partDir2);
        Directory.CreateDirectory(partDir3);

        // パーツ作成
        var fabricated = FabricatedPart.Create(new PartNumber("PLATE-001"), "ベースプレート", partDir1);
        var standard = StandardPart.Create(new PartNumber("BOLT-M10"), "ボルト M10x30", partDir2);
        var purchased = PurchasedPart.Create(new PartNumber("MOTOR-01"), "ACサーボモータ", partDir3);

        _context.Parts.AddRange(fabricated, standard, purchased);

        // 装置作成
        var assetDir = Path.Combine(_tempDir, "test-asset");
        Directory.CreateDirectory(assetDir);
        var asset = Asset.Create("test-asset", assetDir);
        _context.Assets.Add(asset);

        await _context.SaveChangesAsync();

        // 装置にパーツを紐づけ
        var ac1 = AssetComponent.Create(asset.Id, fabricated.Id, 1);
        var ac2 = AssetComponent.Create(asset.Id, standard.Id, 4);
        _context.AssetComponents.AddRange(ac1, ac2);

        await _context.SaveChangesAsync();

        return (asset.Id, new List<Guid> { fabricated.Id, standard.Id, purchased.Id });
    }

    /// <summary>
    /// テスト用 IDbContextFactory 実装。
    /// </summary>
    private class TestDbContextFactory : IDbContextFactory<DesignAidDbContext>
    {
        private readonly DbContextOptions<DesignAidDbContext> _options;

        public TestDbContextFactory(DbContextOptions<DesignAidDbContext> options)
        {
            _options = options;
        }

        public DesignAidDbContext CreateDbContext()
        {
            return new DesignAidDbContext(_options);
        }
    }
}
