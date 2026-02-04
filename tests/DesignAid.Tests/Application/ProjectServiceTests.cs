using DesignAid.Application.Services;
using DesignAid.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DesignAid.Tests.Application;

/// <summary>
/// ProjectService のテスト。
/// </summary>
public class ProjectServiceTests : IDisposable
{
    private readonly DesignAidDbContext _context;
    private readonly ProjectService _service;

    public ProjectServiceTests()
    {
        var options = new DbContextOptionsBuilder<DesignAidDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new DesignAidDbContext(options);
        _service = new ProjectService(_context);
    }

    public void Dispose()
    {
        _context.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task AddAsync_ValidInput_CreatesProject()
    {
        // Arrange
        var name = "test-project";
        var path = @"C:\work\test-project";

        // Act
        var project = await _service.AddAsync(name, path);

        // Assert
        Assert.NotNull(project);
        Assert.NotEqual(Guid.Empty, project.Id);
        Assert.Equal(name, project.Name);

        // DB に保存されていることを確認
        var saved = await _context.Projects.FindAsync(project.Id);
        Assert.NotNull(saved);
    }

    [Fact]
    public async Task AddAsync_DuplicateName_ThrowsInvalidOperationException()
    {
        // Arrange
        var name = "duplicate-project";
        var path1 = @"C:\work\project1";
        var path2 = @"C:\work\project2";

        await _service.AddAsync(name, path1);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.AddAsync(name, path2));
    }

    [Fact]
    public async Task GetByIdAsync_ExistingProject_ReturnsProject()
    {
        // Arrange
        var project = await _service.AddAsync("test", @"C:\work\test");

        // Act
        var result = await _service.GetByIdAsync(project.Id);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(project.Id, result.Id);
    }

    [Fact]
    public async Task GetByIdAsync_NonExistingProject_ReturnsNull()
    {
        // Act
        var result = await _service.GetByIdAsync(Guid.NewGuid());

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetByNameAsync_ExistingProject_ReturnsProject()
    {
        // Arrange
        var name = "find-by-name";
        await _service.AddAsync(name, @"C:\work\find-by-name");

        // Act
        var result = await _service.GetByNameAsync(name);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(name, result.Name);
    }

    [Fact]
    public async Task GetByNameAsync_NonExistingProject_ReturnsNull()
    {
        // Act
        var result = await _service.GetByNameAsync("non-existing");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsAllProjects()
    {
        // Arrange
        await _service.AddAsync("project1", @"C:\work\project1");
        await _service.AddAsync("project2", @"C:\work\project2");
        await _service.AddAsync("project3", @"C:\work\project3");

        // Act
        var result = await _service.GetAllAsync();

        // Assert
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public async Task GetAllAsync_EmptyDatabase_ReturnsEmptyList()
    {
        // Act
        var result = await _service.GetAllAsync();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task UpdateAsync_ValidInput_UpdatesProject()
    {
        // Arrange
        var project = await _service.AddAsync("test", @"C:\work\test");
        var newDisplayName = "更新後の表示名";
        var newDescription = "更新後の説明";

        // Act
        var updated = await _service.UpdateAsync("test", newDisplayName, newDescription);

        // Assert
        Assert.NotNull(updated);
        Assert.Equal(newDisplayName, updated.DisplayName);
        Assert.Equal(newDescription, updated.Description);
    }

    [Fact]
    public async Task UpdateAsync_NonExistingProject_ReturnsNull()
    {
        // Act
        var result = await _service.UpdateAsync("non-existing", "display", "desc");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task RemoveAsync_ExistingProject_DeletesProject()
    {
        // Arrange
        var project = await _service.AddAsync("to-delete", @"C:\work\to-delete");

        // Act
        var removed = await _service.RemoveAsync("to-delete");

        // Assert
        Assert.True(removed);
        var result = await _service.GetByIdAsync(project.Id);
        Assert.Null(result);
    }

    [Fact]
    public async Task RemoveAsync_NonExistingProject_ReturnsFalse()
    {
        // Act
        var removed = await _service.RemoveAsync("non-existing");

        // Assert
        Assert.False(removed);
    }
}
