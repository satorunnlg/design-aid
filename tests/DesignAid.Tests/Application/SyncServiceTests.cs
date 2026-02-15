using DesignAid.Application.Services;
using DesignAid.Domain.Entities;
using DesignAid.Domain.ValueObjects;
using DesignAid.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DesignAid.Tests.Application;

/// <summary>
/// SyncService のテスト。
/// </summary>
public class SyncServiceTests : IDisposable
{
    private readonly DesignAidDbContext _context;
    private readonly HashService _hashService;
    private readonly SyncService _service;
    private readonly string _tempDir;

    public SyncServiceTests()
    {
        var options = new DbContextOptionsBuilder<DesignAidDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new DesignAidDbContext(options);
        _hashService = new HashService();

        // VectorSearchService なしでテスト
        _service = new SyncService(_context, _hashService, null);

        // テスト用一時ディレクトリ
        _tempDir = Path.Combine(Path.GetTempPath(), "design-aid-sync-tests", Guid.NewGuid().ToString());
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
    public async Task SyncAllAsync_EmptyDatabase_ReturnsEmptyResult()
    {
        // Act
        var result = await _service.SyncAllAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(0, result.UpdateCount);
        Assert.Equal(0, result.ErrorCount);
    }

    [Fact]
    public async Task SyncAllAsync_WithParts_SyncsAll()
    {
        // Arrange
        var partDir1 = Path.Combine(_tempDir, "PART-001");
        var partDir2 = Path.Combine(_tempDir, "PART-002");
        Directory.CreateDirectory(partDir1);
        Directory.CreateDirectory(partDir2);

        var part1 = FabricatedPart.Create(new PartNumber("PART-001"), "部品1", partDir1);
        var part2 = FabricatedPart.Create(new PartNumber("PART-002"), "部品2", partDir2);
        _context.Parts.AddRange(part1, part2);
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.SyncAllAsync();

        // Assert
        Assert.Equal(0, result.ErrorCount);
    }

    [Fact]
    public async Task SyncPartAsync_NewArtifacts_UpdatesHashes()
    {
        // Arrange
        var partDir = Path.Combine(_tempDir, "SYNC-001");
        Directory.CreateDirectory(partDir);

        // 成果物ファイルを作成
        var drawingPath = Path.Combine(partDir, "drawing.dxf");
        File.WriteAllText(drawingPath, "DXF content for sync test");

        var part = FabricatedPart.Create(new PartNumber("SYNC-001"), "同期テスト部品", partDir);
        _context.Parts.Add(part);
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.SyncPartAsync(part);

        // Assert
        Assert.Equal(0, result.ErrorCount);
        Assert.True(result.UpdateCount > 0);

        // ハッシュが更新されたことを確認
        var updated = await _context.Parts.FindAsync(part.Id);
        Assert.NotNull(updated);
        Assert.True(updated!.ArtifactHashes.ContainsKey("drawing.dxf"));
    }

    [Fact]
    public async Task SyncPartAsync_NoChanges_ReturnsNoUpdate()
    {
        // Arrange
        var partDir = Path.Combine(_tempDir, "SYNC-002");
        Directory.CreateDirectory(partDir);

        var drawingPath = Path.Combine(partDir, "drawing.dxf");
        File.WriteAllText(drawingPath, "Existing DXF content");

        var hash = _hashService.ComputeHash(drawingPath);

        var part = FabricatedPart.Create(new PartNumber("SYNC-002"), "変更なし部品", partDir);
        part.UpdateArtifactHash("drawing.dxf", hash);
        part.UpdateCurrentHash(hash.Value);
        _context.Parts.Add(part);
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.SyncPartAsync(part);

        // Assert
        Assert.Equal(0, result.ErrorCount);
        Assert.Equal(0, result.UpdateCount);
    }

    [Fact]
    public async Task SyncPartAsync_ModifiedFile_DetectsChange()
    {
        // Arrange
        var partDir = Path.Combine(_tempDir, "SYNC-003");
        Directory.CreateDirectory(partDir);

        var drawingPath = Path.Combine(partDir, "drawing.dxf");
        File.WriteAllText(drawingPath, "Original content");

        var originalHash = _hashService.ComputeHash(drawingPath);

        var part = FabricatedPart.Create(new PartNumber("SYNC-003"), "変更検知部品", partDir);
        part.UpdateArtifactHash("drawing.dxf", originalHash);
        part.UpdateCurrentHash(originalHash.Value);
        _context.Parts.Add(part);
        await _context.SaveChangesAsync();

        // ファイルを変更
        File.WriteAllText(drawingPath, "Modified content");

        // Act
        var result = await _service.SyncPartAsync(part);

        // Assert
        Assert.Equal(0, result.ErrorCount);
        Assert.True(result.UpdateCount > 0);
        Assert.True(result.Updated.ContainsKey("SYNC-003"));
        Assert.Contains("drawing.dxf", result.Updated["SYNC-003"].ModifiedFiles);
    }

    [Fact]
    public async Task SyncPartAsync_NewFile_DetectsAddition()
    {
        // Arrange
        var partDir = Path.Combine(_tempDir, "SYNC-004");
        Directory.CreateDirectory(partDir);

        var part = FabricatedPart.Create(new PartNumber("SYNC-004"), "ファイル追加部品", partDir);
        _context.Parts.Add(part);
        await _context.SaveChangesAsync();

        // 同期後にファイルを追加
        var drawingPath = Path.Combine(partDir, "new-drawing.dxf");
        File.WriteAllText(drawingPath, "New file content");

        // Act
        var result = await _service.SyncPartAsync(part);

        // Assert
        Assert.Equal(0, result.ErrorCount);
        Assert.True(result.UpdateCount > 0);
        Assert.True(result.Updated.ContainsKey("SYNC-004"));
        Assert.Contains("new-drawing.dxf", result.Updated["SYNC-004"].NewFiles);
    }

    [Fact]
    public async Task SyncPartAsync_DeletedFile_DetectsRemoval()
    {
        // Arrange
        var partDir = Path.Combine(_tempDir, "SYNC-005");
        Directory.CreateDirectory(partDir);

        var drawingPath = Path.Combine(partDir, "to-delete.dxf");
        File.WriteAllText(drawingPath, "File to be deleted");

        var hash = _hashService.ComputeHash(drawingPath);

        var part = FabricatedPart.Create(new PartNumber("SYNC-005"), "ファイル削除部品", partDir);
        part.UpdateArtifactHash("to-delete.dxf", hash);
        _context.Parts.Add(part);
        await _context.SaveChangesAsync();

        // ファイルを削除
        File.Delete(drawingPath);

        // Act
        var result = await _service.SyncPartAsync(part);

        // Assert
        Assert.Equal(0, result.ErrorCount);
        Assert.True(result.UpdateCount > 0);
        Assert.True(result.Updated.ContainsKey("SYNC-005"));
        Assert.Contains("to-delete.dxf", result.Updated["SYNC-005"].DeletedFiles);
    }

    [Fact]
    public async Task SyncPartAsync_NonExistingDirectory_ReturnsError()
    {
        // Arrange
        var nonExistingPath = Path.Combine(_tempDir, "NON-EXISTING");

        var part = FabricatedPart.Create(new PartNumber("SYNC-006"), "存在しないディレクトリ", nonExistingPath);
        _context.Parts.Add(part);
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.SyncPartAsync(part);

        // Assert
        // ディレクトリが存在しない場合はエラー
        Assert.True(result.ErrorCount > 0);
        Assert.True(result.Errors.ContainsKey("SYNC-006"));
    }

    [Fact]
    public async Task SyncAllAsync_Force_RecomputesHashes()
    {
        // Arrange
        var partDir = Path.Combine(_tempDir, "FORCE-001");
        Directory.CreateDirectory(partDir);

        var drawingPath = Path.Combine(partDir, "drawing.dxf");
        File.WriteAllText(drawingPath, "Force sync content");

        var hash = _hashService.ComputeHash(drawingPath);

        var part = FabricatedPart.Create(new PartNumber("FORCE-001"), "強制同期部品", partDir);
        part.UpdateArtifactHash("drawing.dxf", hash);
        part.UpdateCurrentHash(hash.Value);
        _context.Parts.Add(part);
        await _context.SaveChangesAsync();

        // Act - 強制同期
        var result = await _service.SyncAllAsync(force: true);

        // Assert - 変更がなくても強制同期で更新される
        Assert.Equal(0, result.ErrorCount);
        Assert.True(result.UpdateCount > 0);
    }

    [Fact]
    public async Task SyncToVectorIndexAsync_NoVectorService_ReturnsZero()
    {
        // Arrange
        var partDir = Path.Combine(_tempDir, "VECTOR-001");
        Directory.CreateDirectory(partDir);

        var part = FabricatedPart.Create(new PartNumber("VECTOR-001"), "ベクトル同期部品", partDir);
        _context.Parts.Add(part);
        await _context.SaveChangesAsync();

        // Act - VectorSearchService なしなのでスキップされる
        var count = await _service.SyncToVectorIndexAsync();

        // Assert - VectorSearchService がないので 0
        Assert.Equal(0, count);
    }
}
