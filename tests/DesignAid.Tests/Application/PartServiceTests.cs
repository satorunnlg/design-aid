using DesignAid.Application.Services;
using DesignAid.Domain.Entities;
using DesignAid.Domain.ValueObjects;
using DesignAid.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DesignAid.Tests.Application;

/// <summary>
/// PartService のテスト。
/// </summary>
public class PartServiceTests : IDisposable
{
    private readonly DesignAidDbContext _context;
    private readonly PartService _service;
    private readonly string _tempDir;

    public PartServiceTests()
    {
        var options = new DbContextOptionsBuilder<DesignAidDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new DesignAidDbContext(options);
        _service = new PartService(_context);

        // テスト用一時ディレクトリ
        _tempDir = Path.Combine(Path.GetTempPath(), "design-aid-tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        _context.Dispose();

        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }

        GC.SuppressFinalize(this);
    }

    #region AddFabricatedPartAsync

    [Fact]
    public async Task AddFabricatedPartAsync_ValidInput_CreatesPart()
    {
        // Arrange
        var partNumber = "FAB-001";
        var name = "テスト製作物";
        var path = Path.Combine(_tempDir, partNumber);
        Directory.CreateDirectory(path);

        // Act
        var part = await _service.AddFabricatedPartAsync(
            partNumber, name, path, material: "SS400", surfaceTreatment: "メッキ");

        // Assert
        Assert.NotNull(part);
        Assert.NotEqual(Guid.Empty, part.Id);
        Assert.Equal(partNumber, part.PartNumber.Value);
        Assert.Equal(name, part.Name);
        Assert.Equal("SS400", part.Material);
        Assert.Equal("メッキ", part.SurfaceTreatment);
        Assert.Equal(PartType.Fabricated, part.Type);
    }

    [Fact]
    public async Task AddFabricatedPartAsync_DuplicatePartNumber_ThrowsInvalidOperationException()
    {
        // Arrange
        var partNumber = "FAB-DUP";
        var path = Path.Combine(_tempDir, partNumber);
        Directory.CreateDirectory(path);

        await _service.AddFabricatedPartAsync(partNumber, "最初の部品", path);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.AddFabricatedPartAsync(partNumber, "重複部品", path));
    }

    #endregion

    #region AddPurchasedPartAsync

    [Fact]
    public async Task AddPurchasedPartAsync_ValidInput_CreatesPart()
    {
        // Arrange
        var partNumber = "PUR-001";
        var name = "テスト購入品";
        var path = Path.Combine(_tempDir, partNumber);
        Directory.CreateDirectory(path);

        // Act
        var part = await _service.AddPurchasedPartAsync(
            partNumber, name, path, manufacturer: "三菱電機", modelNumber: "FR-E720-0.4K");

        // Assert
        Assert.NotNull(part);
        Assert.Equal(partNumber, part.PartNumber.Value);
        Assert.Equal("三菱電機", part.Manufacturer);
        Assert.Equal("FR-E720-0.4K", part.ManufacturerPartNumber);
        Assert.Equal(PartType.Purchased, part.Type);
    }

    #endregion

    #region AddStandardPartAsync

    [Fact]
    public async Task AddStandardPartAsync_ValidInput_CreatesPart()
    {
        // Arrange
        var partNumber = "STD-001";
        var name = "六角ボルト";
        var path = Path.Combine(_tempDir, partNumber);
        Directory.CreateDirectory(path);

        // Act
        var part = await _service.AddStandardPartAsync(
            partNumber, name, path, standardCode: "JIS B1180", size: "M10×30");

        // Assert
        Assert.NotNull(part);
        Assert.Equal(partNumber, part.PartNumber.Value);
        Assert.Equal("JIS B1180", part.StandardNumber);
        Assert.Equal("M10×30", part.Size);
        Assert.Equal(PartType.Standard, part.Type);
    }

    #endregion

    #region GetAllAsync

    [Fact]
    public async Task GetAllAsync_ReturnsAllParts()
    {
        // Arrange
        var path1 = Path.Combine(_tempDir, "P1");
        var path2 = Path.Combine(_tempDir, "P2");
        var path3 = Path.Combine(_tempDir, "P3");
        Directory.CreateDirectory(path1);
        Directory.CreateDirectory(path2);
        Directory.CreateDirectory(path3);

        await _service.AddFabricatedPartAsync("P1", "製作物", path1);
        await _service.AddPurchasedPartAsync("P2", "購入品", path2);
        await _service.AddStandardPartAsync("P3", "規格品", path3);

        // Act
        var parts = await _service.GetAllAsync();

        // Assert
        Assert.Equal(3, parts.Count);
    }

    #endregion

    #region GetByTypeAsync

    [Fact]
    public async Task GetByTypeAsync_Fabricated_ReturnsOnlyFabricatedParts()
    {
        // Arrange
        var path1 = Path.Combine(_tempDir, "FAB-T1");
        var path2 = Path.Combine(_tempDir, "FAB-T2");
        var path3 = Path.Combine(_tempDir, "PUR-T1");
        Directory.CreateDirectory(path1);
        Directory.CreateDirectory(path2);
        Directory.CreateDirectory(path3);

        await _service.AddFabricatedPartAsync("FAB-T1", "製作物1", path1);
        await _service.AddFabricatedPartAsync("FAB-T2", "製作物2", path2);
        await _service.AddPurchasedPartAsync("PUR-T1", "購入品1", path3);

        // Act
        var parts = await _service.GetByTypeAsync(PartType.Fabricated);

        // Assert
        Assert.Equal(2, parts.Count);
        Assert.All(parts, p => Assert.Equal(PartType.Fabricated, p.Type));
    }

    [Fact]
    public async Task GetByTypeAsync_Purchased_ReturnsOnlyPurchasedParts()
    {
        // Arrange
        var path1 = Path.Combine(_tempDir, "PUR-T2");
        var path2 = Path.Combine(_tempDir, "FAB-T3");
        Directory.CreateDirectory(path1);
        Directory.CreateDirectory(path2);

        await _service.AddPurchasedPartAsync("PUR-T2", "購入品", path1);
        await _service.AddFabricatedPartAsync("FAB-T3", "製作物", path2);

        // Act
        var parts = await _service.GetByTypeAsync(PartType.Purchased);

        // Assert
        Assert.Single(parts);
        Assert.Equal(PartType.Purchased, parts[0].Type);
    }

    #endregion

    #region GetByPartNumberAsync

    [Fact]
    public async Task GetByPartNumberAsync_ExistingPart_ReturnsPart()
    {
        // Arrange
        var partNumber = "FIND-001";
        var path = Path.Combine(_tempDir, partNumber);
        Directory.CreateDirectory(path);

        await _service.AddFabricatedPartAsync(partNumber, "検索対象部品", path);

        // Act
        var part = await _service.GetByPartNumberAsync(partNumber);

        // Assert
        Assert.NotNull(part);
        Assert.Equal(partNumber, part.PartNumber.Value);
    }

    [Fact]
    public async Task GetByPartNumberAsync_NonExistingPart_ReturnsNull()
    {
        // Act
        var part = await _service.GetByPartNumberAsync("NON-EXISTING");

        // Assert
        Assert.Null(part);
    }

    #endregion

    #region GetByIdAsync

    [Fact]
    public async Task GetByIdAsync_ExistingPart_ReturnsPart()
    {
        // Arrange
        var partNumber = "ID-001";
        var path = Path.Combine(_tempDir, partNumber);
        Directory.CreateDirectory(path);

        var created = await _service.AddFabricatedPartAsync(partNumber, "ID検索対象", path);

        // Act
        var part = await _service.GetByIdAsync(created.Id);

        // Assert
        Assert.NotNull(part);
        Assert.Equal(created.Id, part.Id);
    }

    [Fact]
    public async Task GetByIdAsync_NonExistingPart_ReturnsNull()
    {
        // Act
        var part = await _service.GetByIdAsync(Guid.NewGuid());

        // Assert
        Assert.Null(part);
    }

    #endregion

    #region RemoveAsync

    [Fact]
    public async Task RemoveAsync_ExistingPart_ReturnsTrue()
    {
        // Arrange
        var partNumber = "DEL-001";
        var path = Path.Combine(_tempDir, partNumber);
        Directory.CreateDirectory(path);

        await _service.AddFabricatedPartAsync(partNumber, "削除対象部品", path);

        // Act
        var result = await _service.RemoveAsync(partNumber);

        // Assert
        Assert.True(result);
        var deleted = await _service.GetByPartNumberAsync(partNumber);
        Assert.Null(deleted);
    }

    [Fact]
    public async Task RemoveAsync_NonExistingPart_ReturnsFalse()
    {
        // Act
        var result = await _service.RemoveAsync("NON-EXISTING");

        // Assert
        Assert.False(result);
    }

    #endregion

    #region LinkToAssetAsync

    [Fact]
    public async Task LinkToAssetAsync_NewLink_CreatesAssetComponent()
    {
        // Arrange
        var assetPath = Path.Combine(_tempDir, "assets", "test-asset");
        Directory.CreateDirectory(assetPath);
        var asset = Asset.Create("test-asset", assetPath);
        _context.Assets.Add(asset);
        await _context.SaveChangesAsync();

        var partPath = Path.Combine(_tempDir, "LINK-001");
        Directory.CreateDirectory(partPath);
        var part = await _service.AddFabricatedPartAsync("LINK-001", "リンク対象部品", partPath);

        // Act
        var link = await _service.LinkToAssetAsync(asset.Id, part.Id, quantity: 5, notes: "テスト備考");

        // Assert
        Assert.Equal(asset.Id, link.AssetId);
        Assert.Equal(part.Id, link.PartId);
        Assert.Equal(5, link.Quantity);
        Assert.Equal("テスト備考", link.Notes);
    }

    [Fact]
    public async Task LinkToAssetAsync_ExistingLink_UpdatesQuantity()
    {
        // Arrange
        var assetPath = Path.Combine(_tempDir, "assets", "update-asset");
        Directory.CreateDirectory(assetPath);
        var asset = Asset.Create("update-asset", assetPath);
        _context.Assets.Add(asset);
        await _context.SaveChangesAsync();

        var partPath = Path.Combine(_tempDir, "UPDATE-LINK");
        Directory.CreateDirectory(partPath);
        var part = await _service.AddFabricatedPartAsync("UPDATE-LINK", "更新対象部品", partPath);

        // 初回リンク
        await _service.LinkToAssetAsync(asset.Id, part.Id, quantity: 1);

        // Act - 同じ組み合わせで再リンク（数量更新）
        var updated = await _service.LinkToAssetAsync(asset.Id, part.Id, quantity: 10, notes: "更新備考");

        // Assert
        Assert.Equal(10, updated.Quantity);
        Assert.Equal("更新備考", updated.Notes);

        // 重複して作られていないことを確認
        var links = await _context.AssetComponents
            .Where(ac => ac.AssetId == asset.Id && ac.PartId == part.Id)
            .ToListAsync();
        Assert.Single(links);
    }

    #endregion

    #region GetPartsByAssetAsync

    [Fact]
    public async Task GetPartsByAssetAsync_ReturnsLinkedParts()
    {
        // Arrange
        var assetPath = Path.Combine(_tempDir, "assets", "parts-asset");
        Directory.CreateDirectory(assetPath);
        var asset = Asset.Create("parts-asset", assetPath);
        _context.Assets.Add(asset);
        await _context.SaveChangesAsync();

        var path1 = Path.Combine(_tempDir, "PA-001");
        var path2 = Path.Combine(_tempDir, "PA-002");
        Directory.CreateDirectory(path1);
        Directory.CreateDirectory(path2);

        var part1 = await _service.AddFabricatedPartAsync("PA-001", "部品1", path1);
        var part2 = await _service.AddPurchasedPartAsync("PA-002", "部品2", path2);

        await _service.LinkToAssetAsync(asset.Id, part1.Id, 2);
        await _service.LinkToAssetAsync(asset.Id, part2.Id, 1);

        // Act
        var parts = await _service.GetPartsByAssetAsync(asset.Id);

        // Assert
        Assert.Equal(2, parts.Count);
        Assert.Contains(parts, p => p.Part.PartNumber.Value == "PA-001" && p.Link.Quantity == 2);
        Assert.Contains(parts, p => p.Part.PartNumber.Value == "PA-002" && p.Link.Quantity == 1);
    }

    [Fact]
    public async Task GetPartsByAssetAsync_NoLinkedParts_ReturnsEmptyList()
    {
        // Arrange
        var assetPath = Path.Combine(_tempDir, "assets", "empty-asset");
        Directory.CreateDirectory(assetPath);
        var asset = Asset.Create("empty-asset", assetPath);
        _context.Assets.Add(asset);
        await _context.SaveChangesAsync();

        // Act
        var parts = await _service.GetPartsByAssetAsync(asset.Id);

        // Assert
        Assert.Empty(parts);
    }

    #endregion
}
