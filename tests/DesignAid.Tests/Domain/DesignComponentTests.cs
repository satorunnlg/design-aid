using DesignAid.Domain.Entities;
using DesignAid.Domain.ValueObjects;

namespace DesignAid.Tests.Domain;

/// <summary>
/// DesignComponent エンティティのテスト。
/// FabricatedPart, PurchasedPart, StandardPart を通じてテスト。
/// </summary>
public class DesignComponentTests
{
    #region FabricatedPart Tests

    [Fact]
    public void FabricatedPart_Create_ReturnsValidPart()
    {
        // Arrange
        var partNumber = new PartNumber("FAB-001");
        var name = "テストプレート";
        var path = @"C:\work\data\components\FAB-001";

        // Act
        var part = FabricatedPart.Create(partNumber, name, path);

        // Assert
        Assert.NotEqual(Guid.Empty, part.Id);
        Assert.Equal("FAB-001", part.PartNumber.Value);
        Assert.Equal(name, part.Name);
        Assert.Equal(PartType.Fabricated, part.Type);
        Assert.Equal("1.0.0", part.Version);
        Assert.Equal(HandoverStatus.Draft, part.Status);
        Assert.Contains("FAB-001", part.DirectoryPath);
    }

    [Fact]
    public void FabricatedPart_UpdateFabricationInfo_SetsMaterial()
    {
        // Arrange
        var partNumber = new PartNumber("FAB-002");
        var name = "SS400プレート";
        var path = @"C:\work\data\components\FAB-002";
        var material = "SS400";

        // Act
        var part = FabricatedPart.Create(partNumber, name, path);
        part.UpdateFabricationInfo(material: material);

        // Assert
        Assert.Equal(material, part.Material);
    }

    [Fact]
    public void FabricatedPart_UpdateFabricationInfo_SetsSurfaceTreatment()
    {
        // Arrange
        var partNumber = new PartNumber("FAB-003");
        var name = "メッキプレート";
        var path = @"C:\work\data\components\FAB-003";
        var surfaceTreatment = "亜鉛メッキ";

        // Act
        var part = FabricatedPart.Create(partNumber, name, path);
        part.UpdateFabricationInfo(surfaceTreatment: surfaceTreatment);

        // Assert
        Assert.Equal(surfaceTreatment, part.SurfaceTreatment);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FabricatedPart_Create_NullOrEmptyName_ThrowsArgumentException(string? name)
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() =>
            FabricatedPart.Create(new PartNumber("FAB-001"), name!, @"C:\work\test"));
        Assert.Contains("パーツ名は必須", ex.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FabricatedPart_Create_NullOrEmptyPath_ThrowsArgumentException(string? path)
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() =>
            FabricatedPart.Create(new PartNumber("FAB-001"), "name", path!));
        Assert.Contains("ディレクトリパスは必須", ex.Message);
    }

    #endregion

    #region PurchasedPart Tests

    [Fact]
    public void PurchasedPart_Create_ReturnsValidPart()
    {
        // Arrange
        var partNumber = new PartNumber("PUR-001");
        var name = "テストモーター";
        var path = @"C:\work\data\components\PUR-001";

        // Act
        var part = PurchasedPart.Create(partNumber, name, path);

        // Assert
        Assert.NotEqual(Guid.Empty, part.Id);
        Assert.Equal("PUR-001", part.PartNumber.Value);
        Assert.Equal(name, part.Name);
        Assert.Equal(PartType.Purchased, part.Type);
    }

    [Fact]
    public void PurchasedPart_UpdatePurchaseInfo_SetsManufacturer()
    {
        // Arrange
        var partNumber = new PartNumber("PUR-002");
        var name = "サーボモーター";
        var path = @"C:\work\data\components\PUR-002";
        var manufacturer = "三菱電機";

        // Act
        var part = PurchasedPart.Create(partNumber, name, path);
        part.UpdatePurchaseInfo(manufacturer: manufacturer);

        // Assert
        Assert.Equal(manufacturer, part.Manufacturer);
    }

    [Fact]
    public void PurchasedPart_UpdatePurchaseInfo_SetsManufacturerPartNumber()
    {
        // Arrange
        var partNumber = new PartNumber("PUR-003");
        var name = "サーボモーター";
        var path = @"C:\work\data\components\PUR-003";
        var mfgPartNumber = "HF-KP43";

        // Act
        var part = PurchasedPart.Create(partNumber, name, path);
        part.UpdatePurchaseInfo(manufacturerPartNumber: mfgPartNumber);

        // Assert
        Assert.Equal(mfgPartNumber, part.ManufacturerPartNumber);
    }

    #endregion

    #region StandardPart Tests

    [Fact]
    public void StandardPart_Create_ReturnsValidPart()
    {
        // Arrange
        var partNumber = new PartNumber("STD-001");
        var name = "六角ボルト";
        var path = @"C:\work\data\components\STD-001";

        // Act
        var part = StandardPart.Create(partNumber, name, path);

        // Assert
        Assert.NotEqual(Guid.Empty, part.Id);
        Assert.Equal("STD-001", part.PartNumber.Value);
        Assert.Equal(name, part.Name);
        Assert.Equal(PartType.Standard, part.Type);
    }

    [Fact]
    public void StandardPart_UpdateStandardInfo_SetsStandardNumber()
    {
        // Arrange
        var partNumber = new PartNumber("STD-002");
        var name = "六角ボルト M8x25";
        var path = @"C:\work\data\components\STD-002";
        var standardNumber = "JIS B1180";

        // Act
        var part = StandardPart.Create(partNumber, name, path);
        part.UpdateStandardInfo(standardNumber: standardNumber);

        // Assert
        Assert.Equal(standardNumber, part.StandardNumber);
    }

    [Fact]
    public void StandardPart_UpdateStandardInfo_SetsSize()
    {
        // Arrange
        var partNumber = new PartNumber("STD-003");
        var name = "六角ボルト";
        var path = @"C:\work\data\components\STD-003";
        var size = "M8x25";

        // Act
        var part = StandardPart.Create(partNumber, name, path);
        part.UpdateStandardInfo(size: size);

        // Assert
        Assert.Equal(size, part.Size);
    }

    #endregion

    #region Common DesignComponent Tests

    [Fact]
    public void ChangeStatus_ValidTransition_ChangesStatus()
    {
        // Arrange
        var part = FabricatedPart.Create(new PartNumber("TEST-001"), "テスト部品", @"C:\work\test");
        Assert.Equal(HandoverStatus.Draft, part.Status);

        // Act
        part.ChangeStatus(HandoverStatus.Ordered);

        // Assert
        Assert.Equal(HandoverStatus.Ordered, part.Status);
    }

    [Fact]
    public void ChangeStatus_InvalidTransition_ThrowsInvalidOperationException()
    {
        // Arrange
        var part = FabricatedPart.Create(new PartNumber("TEST-001"), "テスト部品", @"C:\work\test");

        // Act & Assert - Draft から直接 Delivered には遷移できない
        var ex = Assert.Throws<InvalidOperationException>(() =>
            part.ChangeStatus(HandoverStatus.Delivered));
        Assert.Contains("遷移は無効", ex.Message);
    }

    [Fact]
    public void UpdateVersion_ValidVersion_UpdatesVersion()
    {
        // Arrange
        var part = FabricatedPart.Create(new PartNumber("TEST-001"), "テスト部品", @"C:\work\test");
        var originalUpdatedAt = part.UpdatedAt;
        var newVersion = "2.0.0";

        Thread.Sleep(10);

        // Act
        part.UpdateVersion(newVersion);

        // Assert
        Assert.Equal(newVersion, part.Version);
        Assert.True(part.UpdatedAt > originalUpdatedAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void UpdateVersion_NullOrEmpty_ThrowsArgumentException(string? version)
    {
        // Arrange
        var part = FabricatedPart.Create(new PartNumber("TEST-001"), "テスト部品", @"C:\work\test");

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => part.UpdateVersion(version!));
        Assert.Contains("バージョンは必須", ex.Message);
    }

    [Fact]
    public void UpdateArtifactHash_AddsNewHash()
    {
        // Arrange
        var part = FabricatedPart.Create(new PartNumber("TEST-001"), "テスト部品", @"C:\work\test");
        var relativePath = "drawing.dxf";
        var hash = new FileHash("sha256:e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");

        // Act
        part.UpdateArtifactHash(relativePath, hash);

        // Assert
        Assert.Single(part.ArtifactHashes);
        Assert.True(part.ArtifactHashes.ContainsKey(relativePath));
        Assert.Equal(hash.Value, part.ArtifactHashes[relativePath].Value);
    }

    [Fact]
    public void UpdateArtifactHash_UpdatesExistingHash()
    {
        // Arrange
        var part = FabricatedPart.Create(new PartNumber("TEST-001"), "テスト部品", @"C:\work\test");
        var relativePath = "drawing.dxf";
        var oldHash = new FileHash("sha256:e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");
        var newHash = new FileHash("sha256:abcdef1234567890abcdef1234567890abcdef1234567890abcdef1234567890");

        part.UpdateArtifactHash(relativePath, oldHash);

        // Act
        part.UpdateArtifactHash(relativePath, newHash);

        // Assert
        Assert.Single(part.ArtifactHashes);
        Assert.Equal(newHash.Value, part.ArtifactHashes[relativePath].Value);
    }

    [Fact]
    public void UpdateCurrentHash_SetsHash()
    {
        // Arrange
        var part = FabricatedPart.Create(new PartNumber("TEST-001"), "テスト部品", @"C:\work\test");
        var hash = "sha256:e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

        // Act
        part.UpdateCurrentHash(hash);

        // Assert
        Assert.Equal(hash, part.CurrentHash);
    }

    [Fact]
    public void SetMetadata_AddsMetadata()
    {
        // Arrange
        var part = FabricatedPart.Create(new PartNumber("TEST-001"), "テスト部品", @"C:\work\test");

        // Act
        part.SetMetadata("custom_key", "custom_value");

        // Assert
        Assert.True(part.Metadata.ContainsKey("custom_key"));
        Assert.Equal("custom_value", part.Metadata["custom_key"]);
    }

    [Fact]
    public void GetPartJsonPath_ReturnsCorrectPath()
    {
        // Arrange
        var part = FabricatedPart.Create(new PartNumber("TEST-001"), "テスト部品", @"C:\work\test");

        // Act
        var jsonPath = part.GetPartJsonPath();

        // Assert
        Assert.EndsWith("part.json", jsonPath);
    }

    [Fact]
    public void IsModifiable_DraftStatus_ReturnsTrue()
    {
        // Arrange
        var part = FabricatedPart.Create(new PartNumber("TEST-001"), "テスト部品", @"C:\work\test");

        // Assert
        Assert.True(part.IsModifiable());
    }

    [Fact]
    public void IsModifiable_DeliveredStatus_ReturnsFalse()
    {
        // Arrange
        var part = FabricatedPart.Create(new PartNumber("TEST-001"), "テスト部品", @"C:\work\test");
        part.ChangeStatus(HandoverStatus.Ordered);
        part.ChangeStatus(HandoverStatus.Delivered);

        // Assert
        Assert.False(part.IsModifiable());
    }

    #endregion
}
