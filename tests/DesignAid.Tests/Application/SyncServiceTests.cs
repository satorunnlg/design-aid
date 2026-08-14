using DesignAid.Application.Services;
using DesignAid.Domain.Entities;
using DesignAid.Domain.ValueObjects;
using DesignAid.Infrastructure.FileSystem;
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

    // ------------------------------------------------------------------
    // asset.json と DB の乖離（Issue #1）
    //
    // asset list / asset bom は asset.json しか見ないため、DB にしか無い装置は
    // **存在しないように見える**。エラーも出ないので、検出できることをここで守護する。
    // ------------------------------------------------------------------

    /// <summary>`assets/` 配下に装置ディレクトリを作り、DB にも行を入れる。</summary>
    private async Task<Asset> ArrangeAssetAsync(
        string assetsDir,
        string name,
        bool createDirectory = true,
        AssetStatus status = AssetStatus.Active,
        List<string>? tags = null)
    {
        var assetDir = Path.Combine(assetsDir, name);
        if (createDirectory) Directory.CreateDirectory(assetDir);

        var asset = Asset.Create(name, assetDir, $"表示名:{name}", $"説明:{name}");
        asset.Status = status;
        if (tags != null) asset.Tags = tags;
        _context.Assets.Add(asset);
        await _context.SaveChangesAsync();
        return asset;
    }

    [Fact]
    public async Task FindAssetJsonGaps_DBにあるがJSONが無い_検出する()
    {
        // Arrange
        var assetsDir = Path.Combine(_tempDir, "assets");
        Directory.CreateDirectory(assetsDir);
        var asset = await ArrangeAssetAsync(assetsDir, "gap-asset");

        // Act
        var gaps = _service.FindAssetJsonGaps(assetsDir);

        // Assert
        var gap = Assert.Single(gaps);
        Assert.Equal("gap-asset", gap.Name);
        Assert.Equal(asset.Id, gap.Id);
    }

    [Fact]
    public async Task FindAssetJsonGaps_JSONがあれば_検出しない()
    {
        // Arrange
        var assetsDir = Path.Combine(_tempDir, "assets");
        Directory.CreateDirectory(assetsDir);
        var asset = await ArrangeAssetAsync(assetsDir, "ok-asset");
        new AssetJsonReader().Write(
            Path.Combine(assetsDir, "ok-asset"),
            new AssetJson { Id = asset.Id, Name = "ok-asset" });

        // Act
        var gaps = _service.FindAssetJsonGaps(assetsDir);

        // Assert
        Assert.Empty(gaps);
    }

    [Fact]
    public async Task FindAssetJsonGaps_ディレクトリが無ければ_検出しない()
    {
        // アーカイブ済み（archive/assets/ へ移動済み）を欠落と誤報しないこと。
        // Arrange
        var assetsDir = Path.Combine(_tempDir, "assets");
        Directory.CreateDirectory(assetsDir);
        await ArrangeAssetAsync(assetsDir, "archived-asset", createDirectory: false);

        // Act
        var gaps = _service.FindAssetJsonGaps(assetsDir);

        // Assert
        Assert.Empty(gaps);
    }

    [Fact]
    public void FindAssetJsonGaps_assetsDirが無い_空を返す()
    {
        var gaps = _service.FindAssetJsonGaps(Path.Combine(_tempDir, "not-exist"));
        Assert.Empty(gaps);
    }

    [Fact]
    public async Task RestoreAssetJson_DBと同じIDで復元する()
    {
        // **ID が一致することがこの機能の要**。ここが崩れると Issue #2 と同じ状態に戻る。
        // Arrange
        var assetsDir = Path.Combine(_tempDir, "assets");
        Directory.CreateDirectory(assetsDir);
        var asset = await ArrangeAssetAsync(
            assetsDir, "restore-asset", status: AssetStatus.Dormant, tags: new List<string> { "a", "b" });
        var gaps = _service.FindAssetJsonGaps(assetsDir);

        // Act
        var restored = _service.RestoreAssetJson(gaps);

        // Assert
        Assert.Equal(1, restored);
        var json = new AssetJsonReader().Read(Path.Combine(assetsDir, "restore-asset"));
        Assert.NotNull(json);
        Assert.Equal(asset.Id, json!.Id);
        Assert.Equal("restore-asset", json.Name);
        Assert.Equal("表示名:restore-asset", json.DisplayName);
        Assert.Equal("説明:restore-asset", json.Description);
        Assert.Equal("dormant", json.Status);
        Assert.Equal(new[] { "a", "b" }, json.Tags);
    }

    [Fact]
    public async Task RestoreAssetJson_復元後は検出されない()
    {
        // Arrange
        var assetsDir = Path.Combine(_tempDir, "assets");
        Directory.CreateDirectory(assetsDir);
        await ArrangeAssetAsync(assetsDir, "restore-twice");

        // Act
        _service.RestoreAssetJson(_service.FindAssetJsonGaps(assetsDir));

        // Assert - 冪等（2 回目は対象が無い）
        Assert.Empty(_service.FindAssetJsonGaps(assetsDir));
    }

    [Fact]
    public async Task RestoreAssetJson_既存のJSONを上書きしない()
    {
        // ファイル側が正本である以上、DB の内容で塗り潰してはならない。
        // Arrange - 検出した後に asset.json が作られた状況を作る
        var assetsDir = Path.Combine(_tempDir, "assets");
        Directory.CreateDirectory(assetsDir);
        await ArrangeAssetAsync(assetsDir, "existing-json");
        var gaps = _service.FindAssetJsonGaps(assetsDir);

        var untouchedId = Guid.NewGuid();
        new AssetJsonReader().Write(
            Path.Combine(assetsDir, "existing-json"),
            new AssetJson { Id = untouchedId, Name = "existing-json", DisplayName = "手で書いた値" });

        // Act
        var restored = _service.RestoreAssetJson(gaps);

        // Assert
        Assert.Equal(0, restored);
        var json = new AssetJsonReader().Read(Path.Combine(assetsDir, "existing-json"));
        Assert.Equal(untouchedId, json!.Id);
        Assert.Equal("手で書いた値", json.DisplayName);
    }

    [Fact]
    public async Task RestoreAssetJson_created_atをUTCで書く()
    {
        // asset add は UtcNow を書くので "...Z" になる。復元だけローカルオフセットに
        // なると、生成経路で asset.json の中身が変わる。
        // Arrange
        var assetsDir = Path.Combine(_tempDir, "assets");
        Directory.CreateDirectory(assetsDir);
        await ArrangeAssetAsync(assetsDir, "utc-asset");

        // Act
        _service.RestoreAssetJson(_service.FindAssetJsonGaps(assetsDir));

        // Assert
        var raw = await File.ReadAllTextAsync(Path.Combine(assetsDir, "utc-asset", "asset.json"));
        Assert.Contains("\"created_at\"", raw);
        Assert.Matches(@"""created_at"":\s*""[^""]+Z""", raw);
    }

    [Theory]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Unspecified)]
    [InlineData(DateTimeKind.Local)]
    public void ToUtc_どのKindでもUTCへ揃える(DateTimeKind kind)
    {
        var value = DateTime.SpecifyKind(new DateTime(2026, 8, 15, 3, 33, 22), kind);
        Assert.Equal(DateTimeKind.Utc, SyncService.ToUtc(value).Kind);
    }

    [Fact]
    public void ToUtc_Kindが落ちた値はUTCとみなす()
    {
        // DB には UTC で入れているので、読み直して Kind が落ちた値をローカルとして
        // 変換すると時刻がずれる。
        var value = new DateTime(2026, 8, 15, 3, 33, 22, DateTimeKind.Unspecified);
        var utc = SyncService.ToUtc(value);
        Assert.Equal(value.Ticks, utc.Ticks);
    }

    [Theory]
    [InlineData(AssetStatus.Active, "active")]
    [InlineData(AssetStatus.Dormant, "dormant")]
    [InlineData(AssetStatus.Archived, "archived")]
    [InlineData(AssetStatus.ThirdParty, "third_party")]
    public void ToJsonStatus_全ステータスがasset_jsonの表記に対応する(AssetStatus status, string expected)
    {
        // sync 側のパース（TryParseAssetStatus）と対で保つ。
        Assert.Equal(expected, SyncService.ToJsonStatus(status));
    }
}
