using DesignAid.Application.Services;
using DesignAid.Domain.Entities;
using DesignAid.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DesignAid.Tests.Application;

/// <summary>
/// AssetService のテスト。
/// </summary>
public class AssetServiceTests : IDisposable
{
    private readonly DesignAidDbContext _context;
    private readonly AssetService _service;

    public AssetServiceTests()
    {
        var options = new DbContextOptionsBuilder<DesignAidDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new DesignAidDbContext(options);
        _service = new AssetService(_context);
    }

    public void Dispose()
    {
        _context.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task AddAsync_ValidInput_CreatesAsset()
    {
        // Arrange
        var name = "test-asset";
        var path = @"C:\work\assets\test-asset";

        // Act
        var asset = await _service.AddAsync(name, path);

        // Assert
        Assert.NotNull(asset);
        Assert.NotEqual(Guid.Empty, asset.Id);
        Assert.Equal(name, asset.Name);

        // DB に保存されていることを確認
        var saved = await _context.Assets.FindAsync(asset.Id);
        Assert.NotNull(saved);
    }

    [Fact]
    public async Task AddAsync_DuplicateName_ThrowsInvalidOperationException()
    {
        // Arrange
        var name = "duplicate-asset";
        var path1 = @"C:\work\assets\asset1";
        var path2 = @"C:\work\assets\asset2";

        await _service.AddAsync(name, path1);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.AddAsync(name, path2));
    }

    [Fact]
    public async Task GetByIdAsync_ExistingAsset_ReturnsAsset()
    {
        // Arrange
        var asset = await _service.AddAsync("test", @"C:\work\assets\test");

        // Act
        var result = await _service.GetByIdAsync(asset.Id);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(asset.Id, result.Id);
    }

    [Fact]
    public async Task GetByIdAsync_NonExistingAsset_ReturnsNull()
    {
        // Act
        var result = await _service.GetByIdAsync(Guid.NewGuid());

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsAllAssets()
    {
        // Arrange
        await _service.AddAsync("asset1", @"C:\work\assets\asset1");
        await _service.AddAsync("asset2", @"C:\work\assets\asset2");
        await _service.AddAsync("asset3", @"C:\work\assets\asset3");

        // Act
        var result = await _service.GetAllAsync();

        // Assert
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public async Task GetByNameAsync_ExistingAsset_ReturnsAsset()
    {
        // Arrange
        var name = "find-by-name";
        await _service.AddAsync(name, @"C:\work\assets\find-by-name");

        // Act
        var result = await _service.GetByNameAsync(name);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(name, result.Name);
    }

    [Fact]
    public async Task GetByNameAsync_NonExistingAsset_ReturnsNull()
    {
        // Act
        var result = await _service.GetByNameAsync("non-existing");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task UpdateAsync_ValidInput_UpdatesAsset()
    {
        // Arrange
        var asset = await _service.AddAsync("test", @"C:\work\assets\test");
        var newDisplayName = "更新後の表示名";
        var newDescription = "更新後の説明";

        // Act
        var updated = await _service.UpdateAsync(asset.Id, newDisplayName, newDescription);

        // Assert
        Assert.NotNull(updated);
        Assert.Equal(newDisplayName, updated.DisplayName);
        Assert.Equal(newDescription, updated.Description);
    }

    [Fact]
    public async Task UpdateAsync_NonExistingAsset_ReturnsNull()
    {
        // Act
        var result = await _service.UpdateAsync(Guid.NewGuid(), "display", "desc");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task RemoveAsync_ExistingAsset_DeletesAsset()
    {
        // Arrange
        var asset = await _service.AddAsync("to-delete", @"C:\work\assets\to-delete");

        // Act
        var removed = await _service.RemoveAsync(asset.Id);

        // Assert
        Assert.True(removed);
        var result = await _service.GetByIdAsync(asset.Id);
        Assert.Null(result);
    }

    [Fact]
    public async Task RemoveAsync_NonExistingAsset_ReturnsFalse()
    {
        // Act
        var removed = await _service.RemoveAsync(Guid.NewGuid());

        // Assert
        Assert.False(removed);
    }

    [Fact]
    public async Task AddSubAssetAsync_ValidInput_CreatesRelation()
    {
        // Arrange
        var parentAsset = await _service.AddAsync("parent-asset", @"C:\work\assets\parent");
        var childAsset = await _service.AddAsync("child-asset", @"C:\work\assets\child");

        // Act
        var subAsset = await _service.AddSubAssetAsync(
            parentAsset.Id, childAsset.Id, quantity: 2, notes: "テスト備考");

        // Assert
        Assert.Equal(parentAsset.Id, subAsset.ParentAssetId);
        Assert.Equal(childAsset.Id, subAsset.ChildAssetId);
        Assert.Equal(2, subAsset.Quantity);
        Assert.Equal("テスト備考", subAsset.Notes);
    }

    [Fact]
    public async Task AddSubAssetAsync_SameParentAndChild_ThrowsInvalidOperationException()
    {
        // Arrange
        var asset = await _service.AddAsync("self-asset", @"C:\work\assets\self");

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.AddSubAssetAsync(asset.Id, asset.Id));
    }

    [Fact]
    public async Task AddSubAssetAsync_DuplicateRelation_ThrowsInvalidOperationException()
    {
        // Arrange
        var parentAsset = await _service.AddAsync("parent", @"C:\work\assets\parent");
        var childAsset = await _service.AddAsync("child", @"C:\work\assets\child");

        await _service.AddSubAssetAsync(parentAsset.Id, childAsset.Id);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.AddSubAssetAsync(parentAsset.Id, childAsset.Id));
    }

    [Fact]
    public async Task RemoveSubAssetAsync_ExistingRelation_ReturnsTrue()
    {
        // Arrange
        var parentAsset = await _service.AddAsync("parent", @"C:\work\assets\parent");
        var childAsset = await _service.AddAsync("child", @"C:\work\assets\child");

        await _service.AddSubAssetAsync(parentAsset.Id, childAsset.Id);

        // Act
        var result = await _service.RemoveSubAssetAsync(parentAsset.Id, childAsset.Id);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task RemoveSubAssetAsync_NonExistingRelation_ReturnsFalse()
    {
        // Act
        var result = await _service.RemoveSubAssetAsync(Guid.NewGuid(), Guid.NewGuid());

        // Assert
        Assert.False(result);
    }
}
