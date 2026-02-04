using DesignAid.Domain.Entities;

namespace DesignAid.Tests.Domain;

/// <summary>
/// Project エンティティのテスト。
/// </summary>
public class ProjectTests
{
    [Fact]
    public void Create_ValidInput_ReturnsProject()
    {
        // Arrange
        var name = "test-project";
        var path = @"C:\work\test-project";

        // Act
        var project = Project.Create(name, path);

        // Assert
        Assert.NotEqual(Guid.Empty, project.Id);
        Assert.Equal(name, project.Name);
        Assert.Contains("test-project", project.DirectoryPath);
        Assert.True(project.CreatedAt <= DateTime.UtcNow);
        Assert.True(project.UpdatedAt <= DateTime.UtcNow);
    }

    [Fact]
    public void Create_WithDisplayName_SetsDisplayName()
    {
        // Arrange
        var name = "test-project";
        var path = @"C:\work\test-project";
        var displayName = "テストプロジェクト";

        // Act
        var project = Project.Create(name, path, displayName);

        // Assert
        Assert.Equal(displayName, project.DisplayName);
    }

    [Fact]
    public void Create_WithDescription_SetsDescription()
    {
        // Arrange
        var name = "test-project";
        var path = @"C:\work\test-project";
        var description = "テスト用プロジェクトの説明";

        // Act
        var project = Project.Create(name, path, description: description);

        // Assert
        Assert.Equal(description, project.Description);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_NullOrEmptyName_ThrowsArgumentException(string? name)
    {
        // Arrange
        var path = @"C:\work\test";

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => Project.Create(name!, path));
        Assert.Contains("プロジェクト名は必須", ex.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_NullOrEmptyPath_ThrowsArgumentException(string? path)
    {
        // Arrange
        var name = "test-project";

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => Project.Create(name, path!));
        Assert.Contains("ディレクトリパスは必須", ex.Message);
    }

    [Fact]
    public void Reconstruct_ValidInput_RecreatesProject()
    {
        // Arrange
        var id = Guid.NewGuid();
        var name = "reconstructed";
        var path = @"C:\work\reconstructed";
        var createdAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var updatedAt = new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var displayName = "再構築プロジェクト";

        // Act
        var project = Project.Reconstruct(id, name, path, createdAt, updatedAt, displayName);

        // Assert
        Assert.Equal(id, project.Id);
        Assert.Equal(name, project.Name);
        Assert.Equal(path, project.DirectoryPath);
        Assert.Equal(createdAt, project.CreatedAt);
        Assert.Equal(updatedAt, project.UpdatedAt);
        Assert.Equal(displayName, project.DisplayName);
    }

    [Fact]
    public void Update_DisplayName_UpdatesAndSetsUpdatedAt()
    {
        // Arrange
        var project = Project.Create("test", @"C:\work\test");
        var originalUpdatedAt = project.UpdatedAt;
        var newDisplayName = "更新後の表示名";

        // Wait a bit to ensure UpdatedAt changes
        Thread.Sleep(10);

        // Act
        project.Update(displayName: newDisplayName);

        // Assert
        Assert.Equal(newDisplayName, project.DisplayName);
        Assert.True(project.UpdatedAt > originalUpdatedAt);
    }

    [Fact]
    public void Update_Description_UpdatesDescription()
    {
        // Arrange
        var project = Project.Create("test", @"C:\work\test");
        var newDescription = "更新後の説明";

        // Act
        project.Update(description: newDescription);

        // Assert
        Assert.Equal(newDescription, project.Description);
    }

    [Fact]
    public void UpdatePath_ValidPath_UpdatesPath()
    {
        // Arrange
        var project = Project.Create("test", @"C:\work\test");
        var newPath = @"C:\work\new-location\test";

        // Act
        project.UpdatePath(newPath);

        // Assert
        Assert.Contains("new-location", project.DirectoryPath);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void UpdatePath_NullOrEmpty_ThrowsArgumentException(string? newPath)
    {
        // Arrange
        var project = Project.Create("test", @"C:\work\test");

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => project.UpdatePath(newPath!));
        Assert.Contains("ディレクトリパスは必須", ex.Message);
    }

    [Fact]
    public void AddAsset_ValidInput_AddsAndReturnsAsset()
    {
        // Arrange
        var project = Project.Create("test", @"C:\work\test");
        var assetName = "lifting-unit";

        // Act
        var asset = project.AddAsset(assetName);

        // Assert
        Assert.Single(project.Assets);
        Assert.Equal(assetName, asset.Name);
        Assert.Equal(project.Id, asset.ProjectId);
    }

    [Fact]
    public void AddAsset_WithDisplayName_SetsAssetDisplayName()
    {
        // Arrange
        var project = Project.Create("test", @"C:\work\test");
        var displayName = "昇降ユニット";

        // Act
        var asset = project.AddAsset("lifting-unit", displayName);

        // Assert
        Assert.Equal(displayName, asset.DisplayName);
    }

    [Fact]
    public void GetMarkerFilePath_ReturnsCorrectPath()
    {
        // Arrange
        var project = Project.Create("test", @"C:\work\test");

        // Act
        var markerPath = project.GetMarkerFilePath();

        // Assert
        Assert.EndsWith(".da-project", markerPath);
    }

    [Fact]
    public void GetAssetsDirectoryPath_ReturnsCorrectPath()
    {
        // Arrange
        var project = Project.Create("test", @"C:\work\test");

        // Act
        var assetsPath = project.GetAssetsDirectoryPath();

        // Assert
        Assert.Contains("assets", assetsPath);
    }
}
