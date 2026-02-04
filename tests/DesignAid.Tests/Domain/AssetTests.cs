using DesignAid.Domain.Entities;

namespace DesignAid.Tests.Domain;

/// <summary>
/// Asset エンティティのテスト。
/// </summary>
public class AssetTests
{
    [Fact]
    public void Create_ValidInput_ReturnsAsset()
    {
        // Arrange
        var name = "lifting-unit";
        var path = @"C:\work\test\assets\lifting-unit";

        // Act
        var asset = Asset.Create(name, path);

        // Assert
        Assert.NotEqual(Guid.Empty, asset.Id);
        Assert.Equal(name, asset.Name);
        Assert.Contains("lifting-unit", asset.DirectoryPath);
        Assert.True(asset.CreatedAt <= DateTime.UtcNow);
        Assert.True(asset.UpdatedAt <= DateTime.UtcNow);
    }

    [Fact]
    public void Create_WithDisplayName_SetsDisplayName()
    {
        // Arrange
        var name = "lifting-unit";
        var path = @"C:\work\test\assets\lifting-unit";
        var displayName = "昇降ユニット";

        // Act
        var asset = Asset.Create(name, path, displayName);

        // Assert
        Assert.Equal(displayName, asset.DisplayName);
    }

    [Fact]
    public void Create_WithDescription_SetsDescription()
    {
        // Arrange
        var name = "lifting-unit";
        var path = @"C:\work\test\assets\lifting-unit";
        var description = "昇降機構を担当するユニット";

        // Act
        var asset = Asset.Create(name, path, description: description);

        // Assert
        Assert.Equal(description, asset.Description);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_NullOrEmptyName_ThrowsArgumentException(string? name)
    {
        // Arrange
        var path = @"C:\work\test\assets\test";

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() =>
            Asset.Create(name!, path));
        Assert.Contains("装置名は必須", ex.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_NullOrEmptyPath_ThrowsArgumentException(string? path)
    {
        // Arrange
        var name = "test-asset";

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() =>
            Asset.Create(name, path!));
        Assert.Contains("ディレクトリパスは必須", ex.Message);
    }

    [Fact]
    public void Reconstruct_ValidInput_RecreatesAsset()
    {
        // Arrange
        var id = Guid.NewGuid();
        var name = "reconstructed-asset";
        var path = @"C:\work\test\assets\reconstructed-asset";
        var createdAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var updatedAt = new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var displayName = "再構築装置";

        // Act
        var asset = Asset.Reconstruct(id, name, path, createdAt, updatedAt, displayName);

        // Assert
        Assert.Equal(id, asset.Id);
        Assert.Equal(name, asset.Name);
        Assert.Equal(path, asset.DirectoryPath);
        Assert.Equal(createdAt, asset.CreatedAt);
        Assert.Equal(updatedAt, asset.UpdatedAt);
        Assert.Equal(displayName, asset.DisplayName);
    }

    [Fact]
    public void Update_DisplayName_UpdatesAndSetsUpdatedAt()
    {
        // Arrange
        var asset = Asset.Create("test", @"C:\work\test\assets\test");
        var originalUpdatedAt = asset.UpdatedAt;
        var newDisplayName = "更新後の表示名";

        // Wait a bit to ensure UpdatedAt changes
        Thread.Sleep(10);

        // Act
        asset.Update(displayName: newDisplayName);

        // Assert
        Assert.Equal(newDisplayName, asset.DisplayName);
        Assert.True(asset.UpdatedAt > originalUpdatedAt);
    }

    [Fact]
    public void Update_Description_UpdatesDescription()
    {
        // Arrange
        var asset = Asset.Create("test", @"C:\work\test\assets\test");
        var newDescription = "更新後の説明";

        // Act
        asset.Update(description: newDescription);

        // Assert
        Assert.Equal(newDescription, asset.Description);
    }

    [Fact]
    public void UpdatePath_ValidPath_UpdatesPath()
    {
        // Arrange
        var asset = Asset.Create("test", @"C:\work\test\assets\test");
        var newPath = @"C:\work\new-location\assets\test";

        // Act
        asset.UpdatePath(newPath);

        // Assert
        Assert.Contains("new-location", asset.DirectoryPath);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void UpdatePath_NullOrEmpty_ThrowsArgumentException(string? newPath)
    {
        // Arrange
        var asset = Asset.Create("test", @"C:\work\test\assets\test");

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => asset.UpdatePath(newPath!));
        Assert.Contains("ディレクトリパスは必須", ex.Message);
    }

    [Fact]
    public void GetAssetJsonPath_ReturnsCorrectPath()
    {
        // Arrange
        var asset = Asset.Create("test", @"C:\work\test\assets\test");

        // Act
        var jsonPath = asset.GetAssetJsonPath();

        // Assert
        Assert.EndsWith("asset.json", jsonPath);
    }
}
