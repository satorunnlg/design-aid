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
    private readonly Project _testProject;

    public AssetServiceTests()
    {
        var options = new DbContextOptionsBuilder<DesignAidDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new DesignAidDbContext(options);
        _service = new AssetService(_context);

        // テスト用プロジェクトを作成
        _testProject = Project.Create("test-project", @"C:\work\test-project");
        _context.Projects.Add(_testProject);
        _context.SaveChanges();
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
        var path = @"C:\work\test-project\assets\test-asset";

        // Act
        var asset = await _service.AddAsync(_testProject.Id, name, path);

        // Assert
        Assert.NotNull(asset);
        Assert.NotEqual(Guid.Empty, asset.Id);
        Assert.Equal(name, asset.Name);
        Assert.Equal(_testProject.Id, asset.ProjectId);

        // DB に保存されていることを確認
        var saved = await _context.Assets.FindAsync(asset.Id);
        Assert.NotNull(saved);
    }

    [Fact]
    public async Task AddAsync_DuplicateNameInProject_ThrowsInvalidOperationException()
    {
        // Arrange
        var name = "duplicate-asset";
        var path1 = @"C:\work\test-project\assets\asset1";
        var path2 = @"C:\work\test-project\assets\asset2";

        await _service.AddAsync(_testProject.Id, name, path1);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.AddAsync(_testProject.Id, name, path2));
    }

    [Fact]
    public async Task AddAsync_InvalidProjectId_ThrowsInvalidOperationException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.AddAsync(Guid.NewGuid(), "asset", @"C:\work\test"));
    }

    [Fact]
    public async Task GetByIdAsync_ExistingAsset_ReturnsAsset()
    {
        // Arrange
        var asset = await _service.AddAsync(_testProject.Id, "test", @"C:\work\test-project\assets\test");

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
    public async Task GetByProjectAsync_ReturnsAssetsForProject()
    {
        // Arrange
        await _service.AddAsync(_testProject.Id, "asset1", @"C:\work\test-project\assets\asset1");
        await _service.AddAsync(_testProject.Id, "asset2", @"C:\work\test-project\assets\asset2");

        // 別プロジェクトの装置
        var otherProject = Project.Create("other-project", @"C:\work\other");
        _context.Projects.Add(otherProject);
        await _context.SaveChangesAsync();
        await _service.AddAsync(otherProject.Id, "other-asset", @"C:\work\other\assets\other-asset");

        // Act
        var result = await _service.GetByProjectAsync(_testProject.Id);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.All(result, a => Assert.Equal(_testProject.Id, a.ProjectId));
    }

    [Fact]
    public async Task GetByNameAsync_ExistingAsset_ReturnsAsset()
    {
        // Arrange
        var name = "find-by-name";
        await _service.AddAsync(_testProject.Id, name, @"C:\work\test-project\assets\find-by-name");

        // Act
        var result = await _service.GetByNameAsync(_testProject.Id, name);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(name, result.Name);
    }

    [Fact]
    public async Task GetByNameAsync_NonExistingAsset_ReturnsNull()
    {
        // Act
        var result = await _service.GetByNameAsync(_testProject.Id, "non-existing");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task UpdateAsync_ValidInput_UpdatesAsset()
    {
        // Arrange
        var asset = await _service.AddAsync(_testProject.Id, "test", @"C:\work\test-project\assets\test");
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
        var asset = await _service.AddAsync(_testProject.Id, "to-delete", @"C:\work\test-project\assets\to-delete");

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
}
