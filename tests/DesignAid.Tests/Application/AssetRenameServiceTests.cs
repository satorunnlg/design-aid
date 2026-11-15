using System.Text.Json;
using DesignAid.Application.Services;
using DesignAid.Domain.Entities;
using DesignAid.Domain.ValueObjects;
using DesignAid.Infrastructure.FileSystem;
using DesignAid.Infrastructure.Persistence;
using DesignAid.Infrastructure.VectorSearch;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DesignAid.Tests.Application;

/// <summary>
/// AssetRenameService のテスト。実際のファイルと SQLite（一時）を使う。
/// 実データの DB・assets/ には触れない（すべて一時ディレクトリの中）。
/// </summary>
public class AssetRenameServiceTests : IDisposable
{
    private readonly string _root;
    private readonly string _assetsDir;
    private readonly string _dbPath;
    private readonly AssetJsonReader _json = new();

    public AssetRenameServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "design-aid-rename-tests", Guid.NewGuid().ToString());
        _assetsDir = Path.Combine(_root, "assets");
        _dbPath = Path.Combine(_root, "design_aid.db");
        Directory.CreateDirectory(_assetsDir);

        using var db = NewDb();
        db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_root)) CommandHelperForTests.ForceDelete(_root);
        }
        catch
        {
            // 後始末の失敗は無視
        }
        GC.SuppressFinalize(this);
    }

    private DesignAidDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<DesignAidDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;
        return new DesignAidDbContext(options);
    }

    /// <summary>装置を asset.json と DB の両方に、同じ ID で作る（asset add と同じ状態）。</summary>
    private async Task<Guid> AddAssetAsync(string name, string? display = null, bool inDb = true, bool withJson = true)
    {
        var id = Guid.NewGuid();
        var dir = Path.Combine(_assetsDir, name);
        Directory.CreateDirectory(dir);
        Directory.CreateDirectory(Path.Combine(dir, ".git"));
        await File.WriteAllTextAsync(Path.Combine(dir, ".git", "HEAD"), "ref: refs/heads/main\n");
        await File.WriteAllTextAsync(Path.Combine(dir, "DESIGN.md"), "# " + name);

        if (withJson)
            await _json.CreateAsync(dir, id, name, display ?? name, "説明");

        if (inDb)
        {
            using var db = NewDb();
            db.Assets.Add(Asset.Create(name, dir, display ?? name, "説明", id));
            await db.SaveChangesAsync();
        }

        return id;
    }

    private async Task<AssetRenameResult> RenameAsync(
        string oldName, string newName, Func<string, Task>? hook = null)
    {
        using var db = NewDb();
        var service = new AssetRenameService(db) { StepHook = hook };
        return await service.RenameAsync(_assetsDir, oldName, newName);
    }

    /// <summary>assets/ 以下の全ファイルの相対パスと中身のスナップショット。「何も変わっていない」の判定に使う。</summary>
    private Dictionary<string, string> SnapshotFiles()
    {
        var map = new Dictionary<string, string>();
        foreach (var f in Directory.GetFiles(_assetsDir, "*", SearchOption.AllDirectories))
        {
            map[Path.GetRelativePath(_assetsDir, f).Replace('\\', '/')] = File.ReadAllText(f);
        }
        foreach (var d in Directory.GetDirectories(_assetsDir, "*", SearchOption.AllDirectories))
        {
            map[Path.GetRelativePath(_assetsDir, d).Replace('\\', '/') + "/"] = "";
        }
        return map;
    }

    private async Task<List<(Guid Id, string Name, string Dir, string? Display, string? Prev)>> SnapshotDbAsync()
    {
        using var db = NewDb();
        var rows = await db.Assets.AsNoTracking().OrderBy(a => a.Name).ToListAsync();
        return rows.Select(a => (a.Id, a.Name, a.DirectoryPath, a.DisplayName, a.PreviousNamesRaw)).ToList();
    }

    // ---- 成功の経路 ----

    [Fact]
    public async Task Rename_IDを変えずにフォルダとasset_jsonとDBの名前を変える()
    {
        var id = await AddAssetAsync("old");

        var r = await RenameAsync("old", "new");

        Assert.True(r.Success, string.Join(" / ", r.Errors) + r.Failure);
        Assert.Equal(id, r.AssetId);

        // フォルダ（git ごと）が移った
        Assert.False(Directory.Exists(Path.Combine(_assetsDir, "old")));
        Assert.True(File.Exists(Path.Combine(_assetsDir, "new", ".git", "HEAD")));
        Assert.True(File.Exists(Path.Combine(_assetsDir, "new", "DESIGN.md")));

        // asset.json: 同じ ID・新名・previous_names
        var json = await _json.ReadAsync(Path.Combine(_assetsDir, "new"));
        Assert.NotNull(json);
        Assert.Equal(id, json!.Id);
        Assert.Equal("new", json.Name);
        Assert.Equal("new", json.DisplayName); // 表示名が旧名と同じだったので変わる
        Assert.Equal(new[] { "old" }, json.PreviousNames);

        // DB: 同じ ID・新名・新パス
        using var db = NewDb();
        var row = await db.Assets.AsNoTracking().SingleAsync();
        Assert.Equal(id, row.Id);
        Assert.Equal("new", row.Name);
        Assert.Equal("new", row.DisplayName);
        Assert.Equal(Path.GetFullPath(Path.Combine(_assetsDir, "new")), row.DirectoryPath);
        Assert.Equal(new[] { "old" }, row.PreviousNames);
    }

    [Fact]
    public async Task Rename_利用者が付けた表示名は変えない()
    {
        await AddAssetAsync("old", display: "昇降ユニット");

        var r = await RenameAsync("old", "new");

        Assert.True(r.Success);
        var json = await _json.ReadAsync(Path.Combine(_assetsDir, "new"));
        Assert.Equal("昇降ユニット", json!.DisplayName);
        using var db = NewDb();
        Assert.Equal("昇降ユニット", (await db.Assets.AsNoTracking().SingleAsync()).DisplayName);
    }

    [Fact]
    public async Task Rename_連続で改名するとprevious_namesが古い順に増え_戻すと現在名は外れる()
    {
        await AddAssetAsync("a");

        Assert.True((await RenameAsync("a", "b")).Success);
        Assert.True((await RenameAsync("b", "c")).Success);
        Assert.Equal(new[] { "a", "b" }, (await _json.ReadAsync(Path.Combine(_assetsDir, "c")))!.PreviousNames);

        Assert.True((await RenameAsync("c", "a")).Success);
        var json = await _json.ReadAsync(Path.Combine(_assetsDir, "a"));
        Assert.Equal(new[] { "b", "c" }, json!.PreviousNames);
        using var db = NewDb();
        Assert.Equal(new[] { "b", "c" }, (await db.Assets.AsNoTracking().SingleAsync()).PreviousNames);
    }

    [Fact]
    public async Task Rename_部品と子装置のリンクはIDで結ばれているので引き継がれる()
    {
        var id = await AddAssetAsync("old");
        var childId = await AddAssetAsync("child");
        var partId = Guid.NewGuid();
        using (var db = NewDb())
        {
            var part = FabricatedPart.Create(new PartNumber("P-1"), "部品", Path.Combine(_root, "components", "P-1"));
            db.Parts.Add(part);
            await db.SaveChangesAsync();
            partId = part.Id;
            db.AssetComponents.Add(AssetComponent.Create(id, partId, 3));
            db.AssetSubAssets.Add(new AssetSubAsset
            {
                ParentAssetId = id, ChildAssetId = childId, Quantity = 2, CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var r = await RenameAsync("old", "new");

        Assert.True(r.Success);
        using var check = NewDb();
        var comp = await check.AssetComponents.AsNoTracking().SingleAsync();
        Assert.Equal(id, comp.AssetId);
        Assert.Equal(partId, comp.PartId);
        Assert.Equal(3, comp.Quantity);
        var sub = await check.AssetSubAssets.AsNoTracking().SingleAsync();
        Assert.Equal(id, sub.ParentAssetId);
        Assert.Equal(childId, sub.ChildAssetId);
    }

    [Fact]
    public async Task Rename_改名した装置を子装置として参照する他の装置のasset_linksを書き換える()
    {
        await AddAssetAsync("old");
        await AddAssetAsync("parent");
        await AddAssetAsync("unrelated");
        var parentLinks = Path.Combine(_assetsDir, "parent", "asset_links.json");
        await File.WriteAllTextAsync(parentLinks,
            """{"parts":[],"child_assets":[{"child_asset_name":"old","quantity":2,"notes":"安全"}],"extra":1}""");
        var unrelatedLinks = Path.Combine(_assetsDir, "unrelated", "asset_links.json");
        const string unrelatedText = """{"child_assets":[{"child_asset_name":"other","quantity":1}]}""";
        await File.WriteAllTextAsync(unrelatedLinks, unrelatedText);

        var r = await RenameAsync("old", "new");

        Assert.True(r.Success);
        Assert.Equal(new[] { "parent/asset_links.json" }, r.LinkFilesUpdated);
        var doc = JsonDocument.Parse(await File.ReadAllTextAsync(parentLinks));
        var child = doc.RootElement.GetProperty("child_assets")[0];
        Assert.Equal("new", child.GetProperty("child_asset_name").GetString());
        Assert.Equal(2, child.GetProperty("quantity").GetInt32());
        Assert.Equal(1, doc.RootElement.GetProperty("extra").GetInt32());
        // 無関係なファイルは触らない
        Assert.Equal(unrelatedText, await File.ReadAllTextAsync(unrelatedLinks));
    }

    [Fact]
    public async Task Rename_ベクトル索引の装置名と文書のパスを更新する()
    {
        var id = await AddAssetAsync("old");
        var otherId = await AddAssetAsync("old-x");
        using (var db = NewDb())
        {
            VectorIndexEntry E(Guid aid, string aname, string file) => new()
            {
                PartId = Guid.NewGuid().ToString(),
                PartNumber = $"[DOC] {aname}/{file}",
                Content = "c",
                Embedding = new byte[] { 1, 2, 3, 4 },
                Dimensions = 1,
                AssetId = aid.ToString(),
                AssetName = aname,
                Type = "asset_doc",
                FilePath = $"assets/{aname}/{file}",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.VectorIndex.AddRange(E(id, "old", "DESIGN.md"), E(otherId, "old-x", "DESIGN.md"));
            await db.SaveChangesAsync();
        }

        var r = await RenameAsync("old", "new");

        Assert.True(r.Success);
        Assert.Equal(1, r.VectorRowsUpdated);
        using var check = NewDb();
        var rows = await check.VectorIndex.AsNoTracking().OrderBy(v => v.Id).ToListAsync();
        Assert.Equal("new", rows[0].AssetName);
        Assert.Equal("[DOC] new/DESIGN.md", rows[0].PartNumber);
        Assert.Equal("assets/new/DESIGN.md", rows[0].FilePath);
        // 別の装置の行は変えない
        Assert.Equal("old-x", rows[1].AssetName);
        Assert.Equal("[DOC] old-x/DESIGN.md", rows[1].PartNumber);
    }

    [Fact]
    public async Task Rename_DBだけにある装置_フォルダを移しDBを更新しasset_jsonは作らず警告する()
    {
        var id = await AddAssetAsync("old", withJson: false);

        var r = await RenameAsync("old", "new");

        Assert.True(r.Success);
        Assert.True(Directory.Exists(Path.Combine(_assetsDir, "new")));
        Assert.False(File.Exists(Path.Combine(_assetsDir, "new", "asset.json")));
        Assert.Contains(r.Warnings, w => w.Contains("restore-json"));
        using var db = NewDb();
        var row = await db.Assets.AsNoTracking().SingleAsync();
        Assert.Equal(id, row.Id);
        Assert.Equal("new", row.Name);
        Assert.Equal(new[] { "old" }, row.PreviousNames);
    }

    [Fact]
    public async Task Rename_asset_jsonだけにある装置_フォルダとJSONを更新しDBは触らず警告する()
    {
        var id = await AddAssetAsync("old", inDb: false);

        var r = await RenameAsync("old", "new");

        Assert.True(r.Success);
        var json = await _json.ReadAsync(Path.Combine(_assetsDir, "new"));
        Assert.Equal(id, json!.Id);
        Assert.Equal("new", json.Name);
        Assert.Contains(r.Warnings, w => w.Contains("DB に行がありません"));
        using var db = NewDb();
        Assert.False(await db.Assets.AnyAsync());
    }

    [Fact]
    public async Task Rename_JSONとDBのIDが食い違っていても_それぞれのIDを変えず警告する()
    {
        var jsonId = await AddAssetAsync("old", inDb: false);
        var dbId = Guid.NewGuid();
        using (var db = NewDb())
        {
            db.Assets.Add(Asset.Create("old", Path.Combine(_assetsDir, "old"), "old", "", dbId));
            await db.SaveChangesAsync();
        }

        var r = await RenameAsync("old", "new");

        Assert.True(r.Success);
        Assert.Contains(r.Warnings, w => w.Contains("食い違"));
        Assert.Equal(jsonId, (await _json.ReadAsync(Path.Combine(_assetsDir, "new")))!.Id);
        using var check = NewDb();
        Assert.Equal(dbId, (await check.Assets.AsNoTracking().SingleAsync()).Id);
    }

    // ---- 事前確認で止まる（何も変えない）----

    [Fact]
    public async Task Rename_旧名が無い_何も変えずに止める()
    {
        await AddAssetAsync("other");
        var files = SnapshotFiles();
        var dbRows = await SnapshotDbAsync();

        var r = await RenameAsync("missing", "new");

        Assert.False(r.Success);
        Assert.NotEmpty(r.Errors);
        Assert.Equal(files, SnapshotFiles());
        Assert.Equal(dbRows, await SnapshotDbAsync());
    }

    [Theory]
    [InlineData("db")]       // 新名の装置が DB にある
    [InlineData("json")]     // 新名の装置が asset.json としてある
    [InlineData("folder")]   // assets/<new> が既にある
    [InlineData("file")]     // assets/<new> がファイルとして既にある
    public async Task Rename_新名が既にある_何も変えずに止める(string kind)
    {
        await AddAssetAsync("old");
        switch (kind)
        {
            case "db":
                using (var db = NewDb())
                {
                    db.Assets.Add(Asset.Create("new", Path.Combine(_assetsDir, "elsewhere"), "new", ""));
                    await db.SaveChangesAsync();
                }
                break;
            case "json":
                await AddAssetAsync("new", inDb: false);
                break;
            case "folder":
                Directory.CreateDirectory(Path.Combine(_assetsDir, "new"));
                break;
            case "file":
                await File.WriteAllTextAsync(Path.Combine(_assetsDir, "new"), "x");
                break;
        }
        var files = SnapshotFiles();
        var dbRows = await SnapshotDbAsync();

        var r = await RenameAsync("old", "new");

        Assert.False(r.Success);
        Assert.NotEmpty(r.Errors);
        Assert.Equal(files, SnapshotFiles());
        Assert.Equal(dbRows, await SnapshotDbAsync());
    }

    [Theory]
    [InlineData("old")]      // 同じ
    [InlineData("OLD")]      // 大文字小文字だけの違い
    [InlineData("a/b")]      // 名前の検査に通らない
    [InlineData("CON")]
    [InlineData("")]
    [InlineData("..")]
    public async Task Rename_新名が不正_何も変えずに止める(string newName)
    {
        await AddAssetAsync("old");
        var files = SnapshotFiles();
        var dbRows = await SnapshotDbAsync();

        var r = await RenameAsync("old", newName);

        Assert.False(r.Success);
        Assert.NotEmpty(r.Errors);
        Assert.Equal(files, SnapshotFiles());
        Assert.Equal(dbRows, await SnapshotDbAsync());
    }

    [Fact]
    public async Task Rename_asset_jsonが壊れている_何も変えずに止める()
    {
        await AddAssetAsync("old");
        await File.WriteAllTextAsync(Path.Combine(_assetsDir, "old", "asset.json"), "{ broken");
        var files = SnapshotFiles();

        var r = await RenameAsync("old", "new");

        Assert.False(r.Success);
        Assert.Equal(files, SnapshotFiles());
    }

    // ---- 失敗時の戻し ----

    [Theory]
    [InlineData("after-move")]
    [InlineData("after-json")]
    [InlineData("after-links")]
    [InlineData("after-db-save")]
    public async Task Rename_どの段階で失敗しても_フォルダとJSONと参照とDBが元に戻る(string failAt)
    {
        var id = await AddAssetAsync("old");
        await AddAssetAsync("parent");
        var parentLinks = Path.Combine(_assetsDir, "parent", "asset_links.json");
        await File.WriteAllTextAsync(parentLinks, """{"child_assets":[{"child_asset_name":"old","quantity":1}]}""");
        var files = SnapshotFiles();
        var dbRows = await SnapshotDbAsync();

        var r = await RenameAsync("old", "new",
            step => step == failAt ? throw new InvalidOperationException("注入した失敗") : Task.CompletedTask);

        Assert.False(r.Success);
        Assert.Contains("注入した失敗", r.Failure);
        Assert.Empty(r.RollbackFailures);
        // 全部が元通り（ファイルの中身まで）
        Assert.Equal(files, SnapshotFiles());
        Assert.Equal(dbRows, await SnapshotDbAsync());
        Assert.Equal(id, (await _json.ReadAsync(Path.Combine(_assetsDir, "old")))!.Id);
    }

    [Fact]
    public async Task Rename_戻しの一部が失敗したら_戻せなかった段階は変わったままと報告する()
    {
        await AddAssetAsync("old");
        string? lockedJson = null;

        // asset.json を書き換えた後に失敗させ、その asset.json を読み取り専用にして戻しを失敗させる
        var r = await RenameAsync("old", "new", step =>
        {
            if (step != "after-json") return Task.CompletedTask;
            lockedJson = Path.Combine(_assetsDir, "new", "asset.json");
            File.SetAttributes(lockedJson, FileAttributes.ReadOnly);
            throw new InvalidOperationException("注入した失敗");
        });

        try
        {
            Assert.False(r.Success);
            Assert.Single(r.RollbackFailures);
            Assert.Contains("asset.json", r.RollbackFailures[0]);
            Assert.True(r.AssetJsonUpdated);   // 戻せなかったので変わったまま
            Assert.False(r.FolderMoved);       // フォルダは戻せた
            Assert.Contains("フォルダの移動", r.RolledBack);
        }
        finally
        {
            var path = Path.Combine(_assetsDir, "old", "asset.json");
            if (File.Exists(path)) File.SetAttributes(path, FileAttributes.Normal);
        }
    }

    [Fact]
    public async Task Rename_DBの更新が実際に失敗したら_フォルダとJSONと参照が元に戻る()
    {
        await AddAssetAsync("old");
        await AddAssetAsync("parent");
        var parentLinks = Path.Combine(_assetsDir, "parent", "asset_links.json");
        await File.WriteAllTextAsync(parentLinks, """{"child_assets":[{"child_asset_name":"old","quantity":1}]}""");
        using (var db = NewDb())
        {
            // Name を更新しようとすると DB が拒否する（本物の DB の失敗）
            await db.Database.ExecuteSqlRawAsync(
                "CREATE TRIGGER block_rename BEFORE UPDATE OF Name ON Assets " +
                "BEGIN SELECT RAISE(ABORT, 'ブロックされた'); END;");
        }
        var files = SnapshotFiles();
        var dbRows = await SnapshotDbAsync();

        var r = await RenameAsync("old", "new");

        Assert.False(r.Success);
        Assert.Empty(r.RollbackFailures);
        Assert.Equal(files, SnapshotFiles());
        Assert.Equal(dbRows, await SnapshotDbAsync());
    }

    [Fact]
    public async Task Rename_Windowsでフォルダが開かれていて移動できない_何も変わらず原因を返す()
    {
        if (!OperatingSystem.IsWindows()) return; // 他の OS ではフォルダ内のファイルを開いたままでも移動できる

        await AddAssetAsync("old");
        var files = SnapshotFiles();
        var dbRows = await SnapshotDbAsync();
        var locked = Path.Combine(_assetsDir, "old", "DESIGN.md");

        AssetRenameResult r;
        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            r = await RenameAsync("old", "new");
        }

        Assert.False(r.Success);
        Assert.Contains("移動できません", r.Failure);
        Assert.Empty(r.RollbackFailures);
        Assert.Equal(files, SnapshotFiles());
        Assert.Equal(dbRows, await SnapshotDbAsync());
    }

    // ---- Codex レビュー指摘への検査 ----

    private string ArchiveIndexPath => Path.Combine(_root, "archive_index.json");

    private Task WriteArchiveIndexAsync(params string[] archivedAssetNames)
    {
        var entries = string.Join(",", archivedAssetNames.Select(n =>
            $"{{\"type\":\"asset\",\"name\":\"{n}\",\"archived_at\":\"2026-01-01T00:00:00Z\"}}"));
        return File.WriteAllTextAsync(ArchiveIndexPath, $"{{\"archived\":[{entries}]}}");
    }

    private string WriteArchiveZip(string parentName, string? linksJson)
    {
        var dir = Path.Combine(_root, "archive", "assets");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, parentName + ".zip");
        using var zip = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Create);
        var asset = zip.CreateEntry("asset.json");
        using (var w = new StreamWriter(asset.Open())) w.Write("{}");
        if (linksJson != null)
        {
            var links = zip.CreateEntry("asset_links.json");
            using var w = new StreamWriter(links.Open());
            w.Write(linksJson);
        }
        return path;
    }

    private Dictionary<string, string> SnapshotRoot()
    {
        var map = SnapshotFiles();
        foreach (var f in Directory.GetFiles(_root, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(_root, f).Replace('\\', '/');
            if (rel.StartsWith("assets/") || rel.StartsWith("design_aid.db")) continue;
            map["ROOT:" + rel] = Convert.ToBase64String(File.ReadAllBytes(f));
        }
        return map;
    }

    [Theory]
    [InlineData("abs")]
    [InlineData("../outside/x")]
    [InlineData("..")]
    public async Task Rename_旧名がassetsの外を指す_何も変えずに止める(string kind)
    {
        // assets の外に、asset.json を持つディレクトリを用意する
        var outside = Path.Combine(_root, "outside", "x");
        Directory.CreateDirectory(outside);
        await _json.CreateAsync(outside, Guid.NewGuid(), "x");
        await AddAssetAsync("keep");
        var oldName = kind == "abs" ? outside : kind;
        var files = SnapshotRoot();
        var dbRows = await SnapshotDbAsync();

        var r = await RenameAsync(oldName, "new");

        Assert.False(r.Success);
        Assert.NotEmpty(r.Errors);
        Assert.Equal(files, SnapshotRoot());
        Assert.Equal(dbRows, await SnapshotDbAsync());
        Assert.True(File.Exists(Path.Combine(outside, "asset.json")));
    }

    [Fact]
    public async Task Rename_旧名が単一の通常名なら_assets直下の検査を通って成功する()
    {
        await AddAssetAsync("old", inDb: false); // JSON のみの装置も拒否されない
        var r = await RenameAsync("old", "new");
        Assert.True(r.Success, string.Join(" / ", r.Errors));
    }

    [Fact]
    public async Task Rename_旧名の大文字小文字が違う_補正せずDBの行も行数もリンクも変えずに止める()
    {
        var id = await AddAssetAsync("old");
        var files = SnapshotFiles();
        var dbRows = await SnapshotDbAsync();

        var r = await RenameAsync("OLD", "new");

        Assert.False(r.Success);
        Assert.Contains(r.Errors, e => e.Contains("old")); // 正しい名前を出す
        Assert.Equal(files, SnapshotFiles());
        Assert.Equal(dbRows, await SnapshotDbAsync());
        using var db = NewDb();
        Assert.Equal(id, (await db.Assets.AsNoTracking().SingleAsync()).Id);
    }

    [Fact]
    public async Task Rename_大文字小文字だけ違う別の装置がDBにある_何も変えずに止める()
    {
        await AddAssetAsync("old");
        using (var db = NewDb())
        {
            db.Assets.Add(Asset.Create("OLD", Path.Combine(_root, "elsewhere", "OLD"), "OLD", ""));
            await db.SaveChangesAsync();
        }
        var files = SnapshotFiles();
        var dbRows = await SnapshotDbAsync();

        var r = await RenameAsync("old", "new");

        Assert.False(r.Success);
        Assert.Contains(r.Errors, e => e.Contains("OLD"));
        Assert.Equal(files, SnapshotFiles());
        Assert.Equal(dbRows, await SnapshotDbAsync());
    }

    [Fact]
    public async Task Rename_JSONとDBのIDが違っても_両方のIDの文書索引を更新し無関係な行は触らない()
    {
        var jsonId = await AddAssetAsync("old", inDb: false);
        var dbId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        using (var db = NewDb())
        {
            db.Assets.Add(Asset.Create("old", Path.Combine(_assetsDir, "old"), "old", "", dbId));
            VectorIndexEntry E(Guid aid, string aname, string type, string file) => new()
            {
                PartId = Guid.NewGuid().ToString(),
                PartNumber = $"[DOC] {aname}/{file}",
                Content = "c",
                Embedding = new byte[] { 1 },
                Dimensions = 1,
                AssetId = aid.ToString(),
                AssetName = aname,
                Type = type,
                FilePath = $"assets/{aname}/{file}",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.VectorIndex.AddRange(
                E(jsonId, "old", "asset_doc", "DESIGN.md"),   // 文書索引は asset.json の ID で登録される
                E(dbId, "old", "spec", "x.md"),
                E(otherId, "other", "asset_doc", "DESIGN.md"));
            await db.SaveChangesAsync();
        }

        var r = await RenameAsync("old", "new");

        Assert.True(r.Success, string.Join(" / ", r.Errors) + r.Failure);
        Assert.Equal(2, r.VectorRowsUpdated);
        using var check = NewDb();
        var rows = await check.VectorIndex.AsNoTracking().OrderBy(v => v.Id).ToListAsync();
        Assert.Equal("new", rows[0].AssetName);
        Assert.Equal("[DOC] new/DESIGN.md", rows[0].PartNumber);
        Assert.Equal("assets/new/DESIGN.md", rows[0].FilePath);
        Assert.Equal("new", rows[1].AssetName);
        Assert.Equal("other", rows[2].AssetName);
        Assert.Equal("[DOC] other/DESIGN.md", rows[2].PartNumber);
    }

    [Fact]
    public async Task Rename_アーカイブ済みの装置_何も変えずに止める()
    {
        await AddAssetAsync("old");
        Directory.Delete(Path.Combine(_assetsDir, "old"), true); // archive asset はフォルダを消し、DB の行は残す
        await WriteArchiveIndexAsync("old");
        var files = SnapshotRoot();
        var dbRows = await SnapshotDbAsync();

        var r = await RenameAsync("old", "new");

        Assert.False(r.Success);
        Assert.Contains(r.Errors, e => e.Contains("archive restore"));
        Assert.Equal(files, SnapshotRoot());
        Assert.Equal(dbRows, await SnapshotDbAsync());
    }

    [Fact]
    public async Task Rename_フォルダの無いDBだけの装置は_アーカイブ済みでなければ改名できる()
    {
        await AddAssetAsync("old");
        Directory.Delete(Path.Combine(_assetsDir, "old"), true);
        await WriteArchiveIndexAsync("someone-else"); // 別の装置のアーカイブは妨げない

        var r = await RenameAsync("old", "new");

        Assert.True(r.Success, string.Join(" / ", r.Errors) + r.Failure);
        using var db = NewDb();
        Assert.Equal("new", (await db.Assets.AsNoTracking().SingleAsync()).Name);
    }

    [Fact]
    public async Task Rename_アーカイブ内のasset_linksが旧名を参照している_何も変えずに止める()
    {
        await AddAssetAsync("old");
        var zip = WriteArchiveZip("parent", """{"child_assets":[{"child_asset_name":"old","quantity":1}]}""");
        var files = SnapshotRoot();
        var dbRows = await SnapshotDbAsync();

        var r = await RenameAsync("old", "new");

        Assert.False(r.Success);
        Assert.Contains(r.Errors, e => e.Contains("parent") && e.Contains("archive restore"));
        Assert.Equal(files, SnapshotRoot());
        Assert.Equal(dbRows, await SnapshotDbAsync());
        Assert.True(File.Exists(zip));
    }

    [Fact]
    public async Task Rename_参照の無いアーカイブは改名を妨げない()
    {
        await AddAssetAsync("old");
        WriteArchiveZip("other-parent", """{"child_assets":[{"child_asset_name":"unrelated","quantity":1}]}""");
        WriteArchiveZip("no-links", null);

        var r = await RenameAsync("old", "new");

        Assert.True(r.Success, string.Join(" / ", r.Errors) + r.Failure);
    }

    [Fact]
    public async Task ApplyPendingMigrations_旧スキーマのDBは初回に更新され_適用済みなら何もしない()
    {
        var path = Path.Combine(_root, "old-schema.db");
        var options = new DbContextOptionsBuilder<DesignAidDbContext>().UseSqlite($"Data Source={path}").Options;

        // PreviousNames 列を足す前（AddAssetStatusAndTags まで）のスキーマを作る
        using (var ctx = new DesignAidDbContext(options))
        {
            var migrator = Microsoft.EntityFrameworkCore.Infrastructure.AccessorExtensions
                .GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>(ctx);
            migrator.Migrate("20260219130118_AddAssetStatusAndTags");
        }

        using (var ctx = new DesignAidDbContext(options))
        {
            // 列が無いので、追加後のモデルでの検索は失敗する（ダッシュボードが起動時に受けた失敗）
            await Assert.ThrowsAnyAsync<Exception>(() => ctx.Assets.AsNoTracking().ToListAsync());
            Assert.True(DesignAid.Commands.CommandHelper.ApplyPendingMigrations(ctx));
            Assert.Empty(await ctx.Assets.AsNoTracking().ToListAsync());
        }

        using (var ctx = new DesignAidDbContext(options))
        {
            Assert.False(DesignAid.Commands.CommandHelper.ApplyPendingMigrations(ctx)); // 無条件の Migrate はしない
        }
        SqliteConnection.ClearAllPools();
    }

    // ---- asset.json の往復（previous_names を落とさない）----

    [Fact]
    public async Task AssetJson_previous_namesを書いて読み戻せる()
    {
        var dir = Path.Combine(_assetsDir, "x");
        var id = Guid.NewGuid();
        await _json.WriteAsync(dir, new AssetJson
        {
            Id = id, Name = "x", PreviousNames = new List<string> { "a", "b" }
        });

        var text = await File.ReadAllTextAsync(Path.Combine(dir, "asset.json"));
        Assert.Contains("\"previous_names\"", text);
        Assert.Equal(new[] { "a", "b" }, (await _json.ReadAsync(dir))!.PreviousNames);
    }

    [Fact]
    public async Task AssetJson_改名していない装置にはprevious_namesを書かない()
    {
        var dir = Path.Combine(_assetsDir, "x");
        await _json.CreateAsync(dir, Guid.NewGuid(), "x");

        Assert.DoesNotContain("previous_names", await File.ReadAllTextAsync(Path.Combine(dir, "asset.json")));
    }

    [Fact]
    public async Task AssetJson_updateと同じwith更新でprevious_namesが残る()
    {
        // asset update は `assetJson with { ... }` で書き換える。record の with が欄を落とさないことを固定する
        var dir = Path.Combine(_assetsDir, "x");
        await _json.WriteAsync(dir, new AssetJson { Id = Guid.NewGuid(), Name = "x", PreviousNames = new List<string> { "a" } });

        var read = (await _json.ReadAsync(dir))!;
        await _json.WriteAsync(dir, read with { Description = "更新" });

        var after = (await _json.ReadAsync(dir))!;
        Assert.Equal("更新", after.Description);
        Assert.Equal(new[] { "a" }, after.PreviousNames);
    }

    [Fact]
    public async Task RestoreAssetJson_DBのprevious_namesをasset_jsonへ書く()
    {
        var id = await AddAssetAsync("old", withJson: false);
        Assert.True((await RenameAsync("old", "new")).Success);
        Assert.False(File.Exists(Path.Combine(_assetsDir, "new", "asset.json")));

        using var db = NewDb();
        var sync = new SyncService(db, new HashService());
        var gaps = sync.FindAssetJsonGaps(_assetsDir);
        Assert.Equal(new[] { "old" }, gaps.Single().PreviousNames);
        Assert.Equal(1, sync.RestoreAssetJson(gaps));

        var json = await _json.ReadAsync(Path.Combine(_assetsDir, "new"));
        Assert.Equal(id, json!.Id);
        Assert.Equal(new[] { "old" }, json.PreviousNames);
    }

    [Fact]
    public void Asset_Updateはprevious_namesを渡さなければ変えない()
    {
        var asset = Asset.Create("a", Path.Combine(_root, "a"));
        asset.Update(previousNames: new List<string> { "z" });

        asset.Update(displayName: "表示");

        Assert.Equal(new[] { "z" }, asset.PreviousNames);
    }

    [Fact]
    public void Asset_Renameは名前とパスと履歴を変えIDは変えない()
    {
        var asset = Asset.Create("a", Path.Combine(_root, "a"), "a");
        var id = asset.Id;

        asset.Rename("b", Path.Combine(_root, "b"), "b", new List<string> { "a" });

        Assert.Equal(id, asset.Id);
        Assert.Equal("b", asset.Name);
        Assert.Equal("b", asset.DisplayName);
        Assert.Equal(Path.GetFullPath(Path.Combine(_root, "b")), asset.DirectoryPath);
        Assert.Equal(new[] { "a" }, asset.PreviousNames);
    }
}

/// <summary>テスト後始末用。Windows の git オブジェクトは ReadOnly 属性のことがある。</summary>
internal static class CommandHelperForTests
{
    public static void ForceDelete(string path)
    {
        var dir = new DirectoryInfo(path);
        if (!dir.Exists) return;
        foreach (var f in dir.EnumerateFiles("*", SearchOption.AllDirectories))
        {
            if (f.Attributes.HasFlag(FileAttributes.ReadOnly)) f.Attributes = FileAttributes.Normal;
        }
        dir.Delete(true);
    }
}
