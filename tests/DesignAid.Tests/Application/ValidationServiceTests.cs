using DesignAid.Application.Services;
using DesignAid.Domain.Entities;
using DesignAid.Domain.ValueObjects;
using DesignAid.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DesignAid.Tests.Application;

/// <summary>
/// ValidationService のテスト。
/// </summary>
public class ValidationServiceTests : IDisposable
{
    private readonly DesignAidDbContext _context;
    private readonly HashService _hashService;
    private readonly ValidationService _service;
    private readonly string _tempDir;

    public ValidationServiceTests()
    {
        var options = new DbContextOptionsBuilder<DesignAidDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new DesignAidDbContext(options);
        _hashService = new HashService();
        _service = new ValidationService(_context, _hashService);

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

    [Fact]
    public async Task VerifyAllAsync_EmptyDatabase_ReturnsEmptyResult()
    {
        // Act
        var result = await _service.VerifyAllAsync();

        // Assert
        Assert.Empty(result.Results);
    }

    [Fact]
    public async Task VerifyAllAsync_WithParts_ReturnsResults()
    {
        // Arrange
        var partDir = Path.Combine(_tempDir, "TEST-001");
        Directory.CreateDirectory(partDir);

        var part = FabricatedPart.Create(new PartNumber("TEST-001"), "テスト部品", partDir);
        _context.Parts.Add(part);
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.VerifyAllAsync();

        // Assert
        Assert.True(result.Results.Count > 0);
    }

    [Fact]
    public async Task VerifyPartAsync_ValidPart_ReturnsResult()
    {
        // Arrange
        var partDir = Path.Combine(_tempDir, "TEST-002");
        Directory.CreateDirectory(partDir);

        var part = FabricatedPart.Create(new PartNumber("TEST-002"), "SS400プレート", partDir);
        _context.Parts.Add(part);
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.VerifyPartAsync(part);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Results.ContainsKey("TEST-002"));
    }

    [Fact]
    public async Task VerifyByPartNumberAsync_ExistingPart_ReturnsResult()
    {
        // Arrange
        var partDir = Path.Combine(_tempDir, "TEST-003");
        Directory.CreateDirectory(partDir);

        var part = FabricatedPart.Create(new PartNumber("TEST-003"), "テスト部品", partDir);
        _context.Parts.Add(part);
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.VerifyByPartNumberAsync("TEST-003");

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Results.ContainsKey("TEST-003"));
    }

    [Fact]
    public async Task VerifyByPartNumberAsync_NonExistingPart_ReturnsResultWithError()
    {
        // Act
        var result = await _service.VerifyByPartNumberAsync("NON-EXISTING");

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Results.ContainsKey("NON-EXISTING"));
    }

    [Fact]
    public async Task VerifyIntegrityAsync_NoArtifacts_ReturnsOk()
    {
        // Arrange
        var partDir = Path.Combine(_tempDir, "TEST-004");
        Directory.CreateDirectory(partDir);

        var part = FabricatedPart.Create(new PartNumber("TEST-004"), "成果物なし部品", partDir);
        _context.Parts.Add(part);
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.VerifyIntegrityAsync(part);

        // Assert
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task VerifyIntegrityAsync_MatchingHash_ReturnsOk()
    {
        // Arrange
        var partDir = Path.Combine(_tempDir, "TEST-005");
        Directory.CreateDirectory(partDir);

        var drawingPath = Path.Combine(partDir, "drawing.dxf");
        File.WriteAllText(drawingPath, "DXF content");

        var hash = _hashService.ComputeHash(drawingPath);

        var part = FabricatedPart.Create(new PartNumber("TEST-005"), "ハッシュ一致部品", partDir);
        part.UpdateArtifactHash("drawing.dxf", hash);
        _context.Parts.Add(part);
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.VerifyIntegrityAsync(part);

        // Assert
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task VerifyIntegrityAsync_MismatchingHash_ReturnsWarning()
    {
        // Arrange
        var partDir = Path.Combine(_tempDir, "TEST-006");
        Directory.CreateDirectory(partDir);

        var drawingPath = Path.Combine(partDir, "drawing.dxf");
        File.WriteAllText(drawingPath, "DXF content");

        // 間違ったハッシュを設定
        var wrongHash = new FileHash("sha256:0000000000000000000000000000000000000000000000000000000000000000");

        var part = FabricatedPart.Create(new PartNumber("TEST-006"), "ハッシュ不一致部品", partDir);
        part.UpdateArtifactHash("drawing.dxf", wrongHash);
        _context.Parts.Add(part);
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.VerifyIntegrityAsync(part);

        // Assert
        Assert.True(result.HasWarnings);
    }

    [Fact]
    public async Task VerifyIntegrityAsync_MissingFile_ReturnsError()
    {
        // Arrange
        var partDir = Path.Combine(_tempDir, "TEST-007");
        Directory.CreateDirectory(partDir);

        var hash = new FileHash("sha256:e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");

        var part = FabricatedPart.Create(new PartNumber("TEST-007"), "ファイル不存在部品", partDir);
        part.UpdateArtifactHash("missing.dxf", hash);
        _context.Parts.Add(part);
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.VerifyIntegrityAsync(part);

        // Assert
        Assert.True(result.HasErrors);
    }

    [Fact]
    public async Task VerifyByAssetAsync_ReturnsResultsForAsset()
    {
        // Arrange
        var assetPath = Path.Combine(_tempDir, "assets", "test-asset");
        Directory.CreateDirectory(assetPath);
        var asset = Asset.Create("test-asset", assetPath);
        _context.Assets.Add(asset);

        var partDir = Path.Combine(_tempDir, "components", "TEST-008");
        Directory.CreateDirectory(partDir);

        var part = FabricatedPart.Create(new PartNumber("TEST-008"), "装置部品", partDir);
        _context.Parts.Add(part);

        var assetComponent = AssetComponent.Create(asset.Id, part.Id, 1);
        _context.AssetComponents.Add(assetComponent);
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.VerifyByAssetAsync(asset.Id);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Results.Count > 0);
    }

    [Fact]
    public async Task VerifyByAssetAsync_WithMultipleParts_ReturnsAllResults()
    {
        // Arrange
        var assetPath = Path.Combine(_tempDir, "assets", "test-asset2");
        Directory.CreateDirectory(assetPath);
        var asset = Asset.Create("test-asset2", assetPath);
        _context.Assets.Add(asset);

        var partDir1 = Path.Combine(_tempDir, "components", "TEST-009");
        Directory.CreateDirectory(partDir1);
        var part1 = FabricatedPart.Create(new PartNumber("TEST-009"), "装置部品1", partDir1);

        var partDir2 = Path.Combine(_tempDir, "components", "TEST-010");
        Directory.CreateDirectory(partDir2);
        var part2 = FabricatedPart.Create(new PartNumber("TEST-010"), "装置部品2", partDir2);

        _context.Parts.AddRange(part1, part2);

        var ac1 = AssetComponent.Create(asset.Id, part1.Id, 1);
        var ac2 = AssetComponent.Create(asset.Id, part2.Id, 2);
        _context.AssetComponents.AddRange(ac1, ac2);
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.VerifyByAssetAsync(asset.Id);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Results.Count);
    }

    [Fact]
    public void GetAvailableStandards_ReturnsStandards()
    {
        // Act
        var standards = _service.GetAvailableStandards();

        // Assert
        Assert.NotEmpty(standards);
    }

    [Fact]
    public void GetStandard_ExistingStandard_ReturnsStandard()
    {
        // Act
        var standard = _service.GetStandard("STD-MATERIAL-01");

        // Assert
        Assert.NotNull(standard);
    }

    [Fact]
    public void GetStandard_NonExistingStandard_ReturnsNull()
    {
        // Act
        var standard = _service.GetStandard("NON-EXISTING");

        // Assert
        Assert.Null(standard);
    }
}
