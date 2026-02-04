using DesignAid.Application.Services;
using DesignAid.Domain.Entities;
using DesignAid.Domain.ValueObjects;
using DesignAid.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DesignAid.Tests.Integration;

/// <summary>
/// SQLite 統合テスト。
/// 実際のファイルベース SQLite を使用してテスト。
/// </summary>
public class SqliteIntegrationTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DesignAidDbContext _context;

    public SqliteIntegrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"design_aid_test_{Guid.NewGuid()}.db");

        var options = new DbContextOptionsBuilder<DesignAidDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;

        _context = new DesignAidDbContext(options);
        _context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _context.Dispose();

        // SQLite の接続プールをクリアしてファイルロックを解放
        SqliteConnection.ClearAllPools();

        if (File.Exists(_dbPath))
        {
            try
            {
                File.Delete(_dbPath);
            }
            catch
            {
                // ファイル削除に失敗しても無視
            }
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Database_CreatesAllTables()
    {
        // Assert - テーブルが作成されていることを確認
        Assert.True(await _context.Projects.AnyAsync() == false);
        Assert.True(await _context.Assets.AnyAsync() == false);
        Assert.True(await _context.Parts.AnyAsync() == false);
        Assert.True(await _context.AssetComponents.AnyAsync() == false);
        Assert.True(await _context.HandoverHistory.AnyAsync() == false);
    }

    [Fact]
    public async Task Project_CRUD_WorksCorrectly()
    {
        // Create
        var project = Project.Create("test-project", @"C:\work\test");
        _context.Projects.Add(project);
        await _context.SaveChangesAsync();

        // Read
        var read = await _context.Projects.FindAsync(project.Id);
        Assert.NotNull(read);
        Assert.Equal("test-project", read.Name);

        // Update
        read.Update(displayName: "テストプロジェクト");
        await _context.SaveChangesAsync();

        var updated = await _context.Projects.FindAsync(project.Id);
        Assert.Equal("テストプロジェクト", updated!.DisplayName);

        // Delete
        _context.Projects.Remove(updated);
        await _context.SaveChangesAsync();

        var deleted = await _context.Projects.FindAsync(project.Id);
        Assert.Null(deleted);
    }

    [Fact]
    public async Task Asset_WithProject_WorksCorrectly()
    {
        // Arrange
        var project = Project.Create("project-with-assets", @"C:\work\project");
        _context.Projects.Add(project);
        await _context.SaveChangesAsync();

        // Act
        var asset = Asset.Create(project.Id, "lifting-unit", @"C:\work\project\assets\lifting-unit");
        _context.Assets.Add(asset);
        await _context.SaveChangesAsync();

        // Assert
        var loaded = await _context.Assets
            .Include(a => a.Project)
            .FirstOrDefaultAsync(a => a.Id == asset.Id);

        Assert.NotNull(loaded);
        Assert.NotNull(loaded.Project);
        Assert.Equal(project.Id, loaded.Project.Id);
    }

    [Fact]
    public async Task Part_TPH_Inheritance_WorksCorrectly()
    {
        // Arrange & Act - 各タイプのパーツを作成
        var fabricated = FabricatedPart.Create(new PartNumber("FAB-001"), "製作物", @"C:\work\FAB-001");
        fabricated.Material = "SS400";

        var purchased = PurchasedPart.Create(new PartNumber("PUR-001"), "購入品", @"C:\work\PUR-001");
        purchased.Manufacturer = "三菱";

        var standard = StandardPart.Create(new PartNumber("STD-001"), "規格品", @"C:\work\STD-001");
        standard.StandardNumber = "JIS B1180";

        _context.Parts.AddRange(fabricated, purchased, standard);
        await _context.SaveChangesAsync();

        // Assert - 各タイプが正しく保存・読み込みされること
        var loadedFab = await _context.Parts.OfType<FabricatedPart>().FirstOrDefaultAsync(p => p.Id == fabricated.Id);
        var loadedPur = await _context.Parts.OfType<PurchasedPart>().FirstOrDefaultAsync(p => p.Id == purchased.Id);
        var loadedStd = await _context.Parts.OfType<StandardPart>().FirstOrDefaultAsync(p => p.Id == standard.Id);

        Assert.NotNull(loadedFab);
        Assert.Equal("SS400", loadedFab.Material);
        Assert.Equal(PartType.Fabricated, loadedFab.Type);

        Assert.NotNull(loadedPur);
        Assert.Equal("三菱", loadedPur.Manufacturer);
        Assert.Equal(PartType.Purchased, loadedPur.Type);

        Assert.NotNull(loadedStd);
        Assert.Equal("JIS B1180", loadedStd.StandardNumber);
        Assert.Equal(PartType.Standard, loadedStd.Type);
    }

    [Fact]
    public async Task AssetComponent_ManyToMany_WorksCorrectly()
    {
        // Arrange
        var project = Project.Create("many-to-many", @"C:\work\m2m");
        _context.Projects.Add(project);

        var asset1 = Asset.Create(project.Id, "asset1", @"C:\work\m2m\assets\asset1");
        var asset2 = Asset.Create(project.Id, "asset2", @"C:\work\m2m\assets\asset2");
        _context.Assets.AddRange(asset1, asset2);

        var part = FabricatedPart.Create(new PartNumber("SHARED-001"), "共有部品", @"C:\work\m2m\components\SHARED-001");
        _context.Parts.Add(part);

        await _context.SaveChangesAsync();

        // Act - 同じ部品を2つの装置に関連付け
        var ac1 = AssetComponent.Create(asset1.Id, part.Id, 2);
        var ac2 = AssetComponent.Create(asset2.Id, part.Id, 3);
        _context.AssetComponents.AddRange(ac1, ac2);
        await _context.SaveChangesAsync();

        // Assert
        var partWithAssets = await _context.Parts
            .Include(p => p.AssetComponents)
            .ThenInclude(ac => ac.Asset)
            .FirstOrDefaultAsync(p => p.Id == part.Id);

        Assert.NotNull(partWithAssets);
        Assert.Equal(2, partWithAssets.AssetComponents.Count);
        Assert.Contains(partWithAssets.AssetComponents, ac => ac.Quantity == 2);
        Assert.Contains(partWithAssets.AssetComponents, ac => ac.Quantity == 3);
    }

    [Fact]
    public async Task HandoverRecord_WorksCorrectly()
    {
        // Arrange
        var part = FabricatedPart.Create(new PartNumber("ORDER-001"), "手配テスト部品", @"C:\work\ORDER-001");
        _context.Parts.Add(part);
        await _context.SaveChangesAsync();

        // Act
        var record = HandoverRecord.CreateOrder(
            part.Id,
            "sha256:e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");
        _context.HandoverHistory.Add(record);
        await _context.SaveChangesAsync();

        // Assert
        var loaded = await _context.HandoverHistory
            .Include(h => h.Part)
            .FirstOrDefaultAsync(h => h.PartId == part.Id);

        Assert.NotNull(loaded);
        Assert.Equal(HandoverStatus.Ordered, loaded.Status);
        Assert.NotNull(loaded.Part);
    }

    [Fact]
    public async Task Query_FilterByPartType_WorksCorrectly()
    {
        // Arrange
        var fab1 = FabricatedPart.Create(new PartNumber("FAB-Q1"), "製作物1", @"C:\work\FAB-Q1");
        var fab2 = FabricatedPart.Create(new PartNumber("FAB-Q2"), "製作物2", @"C:\work\FAB-Q2");
        var pur = PurchasedPart.Create(new PartNumber("PUR-Q1"), "購入品1", @"C:\work\PUR-Q1");
        _context.Parts.AddRange(fab1, fab2, pur);
        await _context.SaveChangesAsync();

        // Act - OfType<T>() を使用して型でフィルター
        var fabricatedParts = await _context.Parts
            .OfType<FabricatedPart>()
            .ToListAsync();

        // Assert
        Assert.Equal(2, fabricatedParts.Count);
        Assert.All(fabricatedParts, p => Assert.Equal(PartType.Fabricated, p.Type));
    }

    [Fact]
    public async Task Query_FilterByStatus_WorksCorrectly()
    {
        // Arrange
        var draft = FabricatedPart.Create(new PartNumber("STAT-01"), "Draft", @"C:\work\STAT-01");
        var ordered = FabricatedPart.Create(new PartNumber("STAT-02"), "Ordered", @"C:\work\STAT-02");
        ordered.ChangeStatus(HandoverStatus.Ordered);
        _context.Parts.AddRange(draft, ordered);
        await _context.SaveChangesAsync();

        // Act
        var draftParts = await _context.Parts
            .Where(p => p.Status == HandoverStatus.Draft)
            .ToListAsync();

        // Assert
        Assert.Single(draftParts);
        Assert.Equal("STAT-01", draftParts[0].PartNumber.Value);
    }

    [Fact]
    public async Task CascadeDelete_Asset_DeletesAssetComponents()
    {
        // Arrange
        var project = Project.Create("cascade", @"C:\work\cascade");
        _context.Projects.Add(project);

        var asset = Asset.Create(project.Id, "to-delete", @"C:\work\cascade\assets\to-delete");
        _context.Assets.Add(asset);

        var part = FabricatedPart.Create(new PartNumber("CASCADE-001"), "カスケード部品", @"C:\work\CASCADE-001");
        _context.Parts.Add(part);

        await _context.SaveChangesAsync();

        var ac = AssetComponent.Create(asset.Id, part.Id, 1);
        _context.AssetComponents.Add(ac);
        await _context.SaveChangesAsync();

        // Act
        _context.Assets.Remove(asset);
        await _context.SaveChangesAsync();

        // Assert - AssetComponent も削除されていること
        var remainingAcs = await _context.AssetComponents.Where(x => x.AssetId == asset.Id).ToListAsync();
        Assert.Empty(remainingAcs);

        // Part は残っていること
        var remainingPart = await _context.Parts.FindAsync(part.Id);
        Assert.NotNull(remainingPart);
    }

    [Fact]
    public async Task UniqueConstraint_ProjectName_Enforced()
    {
        // Arrange
        var project1 = Project.Create("unique-name", @"C:\work\unique1");
        _context.Projects.Add(project1);
        await _context.SaveChangesAsync();

        // Act & Assert
        var project2 = Project.Create("unique-name", @"C:\work\unique2");
        _context.Projects.Add(project2);

        await Assert.ThrowsAsync<DbUpdateException>(() => _context.SaveChangesAsync());
    }

    [Fact]
    public async Task UniqueConstraint_PartNumber_Enforced()
    {
        // Arrange
        var part1 = FabricatedPart.Create(new PartNumber("UNIQUE-PN"), "部品1", @"C:\work\UNIQUE1");
        _context.Parts.Add(part1);
        await _context.SaveChangesAsync();

        // Act & Assert
        var part2 = FabricatedPart.Create(new PartNumber("UNIQUE-PN"), "部品2", @"C:\work\UNIQUE2");
        _context.Parts.Add(part2);

        await Assert.ThrowsAsync<DbUpdateException>(() => _context.SaveChangesAsync());
    }
}
