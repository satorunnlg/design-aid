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
        // 文書チャンクの行は消える（次の sync --include-vectors が新しい名前のキーで作り直す）。別の装置の行は変えない
        var left = Assert.Single(rows);
        Assert.Equal("old-x", left.AssetName);
        Assert.Equal("[DOC] old-x/DESIGN.md", left.PartNumber);
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
            if (rel == DesignAid.Application.Services.ProjectLock.FileName) continue; // ロックファイルは置いたままにする
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
        Assert.Equal(2, rows.Count); // JSON の ID の文書行は消え、DB の ID の spec 行は残る
        Assert.Equal("new", rows[0].AssetName);
        Assert.Equal("spec", rows[0].Type);
        Assert.Equal("other", rows[1].AssetName);
        Assert.Equal("[DOC] other/DESIGN.md", rows[1].PartNumber);
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

    // ---- Codex 2 周目の指摘への検査 ----

    private static VectorIndexEntry VecRow(Guid aid, string aname, string type = "asset_doc") => new()
    {
        PartId = Guid.NewGuid().ToString(),
        PartNumber = $"[DOC] {aname}/DESIGN.md",
        Content = "c",
        Embedding = new byte[] { 1 },
        Dimensions = 1,
        AssetId = aid.ToString(),
        AssetName = aname,
        Type = type,
        FilePath = $"assets/{aname}/DESIGN.md",
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    [Fact]
    public async Task Rename_参照を持つzipが開かれていて検査できない_何も変えずに止める()
    {
        await AddAssetAsync("old");
        var zip = WriteArchiveZip("parent", """{"child_assets":[{"child_asset_name":"old","quantity":1}]}""");
        var files = SnapshotRoot();
        var dbRows = await SnapshotDbAsync();

        AssetRenameResult r;
        using (new FileStream(zip, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            r = await RenameAsync("old", "new");
        }

        Assert.False(r.Success);
        Assert.Contains(r.Errors, e => e.Contains("検査できないアーカイブ") && e.Contains("parent"));
        Assert.Equal(files, SnapshotRoot());
        Assert.Equal(dbRows, await SnapshotDbAsync());
    }

    [Fact]
    public async Task Rename_壊れたzipがある_検査できないので何も変えずに止める()
    {
        await AddAssetAsync("old");
        Directory.CreateDirectory(Path.Combine(_root, "archive", "assets"));
        await File.WriteAllBytesAsync(Path.Combine(_root, "archive", "assets", "broken.zip"), new byte[] { 1, 2, 3, 4, 5 });
        var files = SnapshotRoot();

        var r = await RenameAsync("old", "new");

        Assert.False(r.Success);
        Assert.Contains(r.Errors, e => e.Contains("検査できないアーカイブ"));
        Assert.Equal(files, SnapshotRoot());
    }

    [Fact]
    public async Task Rename_読める無関係なzipは_別のzipが開かれていなくても改名を妨げない()
    {
        await AddAssetAsync("old");
        WriteArchiveZip("readable", """{"child_assets":[{"child_asset_name":"unrelated","quantity":1}]}""");

        var r = await RenameAsync("old", "new");

        Assert.True(r.Success, string.Join(" / ", r.Errors) + r.Failure);
    }

    [Fact]
    public async Task Rename_Assetsに行が無いJSONだけの装置でも_文書索引を更新しAssetsの行は作らない()
    {
        var jsonId = await AddAssetAsync("old", inDb: false);
        var otherId = Guid.NewGuid();
        using (var db = NewDb())
        {
            db.VectorIndex.AddRange(VecRow(jsonId, "old"), VecRow(otherId, "other"));
            await db.SaveChangesAsync();
        }

        var r = await RenameAsync("old", "new");

        Assert.True(r.Success, string.Join(" / ", r.Errors) + r.Failure);
        Assert.Equal(1, r.VectorRowsUpdated);
        using var check = NewDb();
        Assert.False(await check.Assets.AnyAsync()); // Assets の行は新しく作らない
        var rows = await check.VectorIndex.AsNoTracking().OrderBy(v => v.Id).ToListAsync();
        Assert.Equal("other", Assert.Single(rows).AssetName); // JSON の ID の文書行は消え、別の装置の行は残る
    }

    [Fact]
    public async Task Rename_JSONだけの装置の索引更新が失敗したら_索引もフォルダも戻る()
    {
        var jsonId = await AddAssetAsync("old", inDb: false);
        using (var db = NewDb())
        {
            db.VectorIndex.Add(VecRow(jsonId, "old"));
            await db.SaveChangesAsync();
        }
        var files = SnapshotFiles();

        var r = await RenameAsync("old", "new",
            step => step == "after-db-save" ? throw new InvalidOperationException("注入した失敗") : Task.CompletedTask);

        Assert.False(r.Success);
        Assert.Equal(files, SnapshotFiles());
        using var check = NewDb();
        var row = await check.VectorIndex.AsNoTracking().SingleAsync();
        Assert.Equal("old", row.AssetName);
        Assert.Equal("assets/old/DESIGN.md", row.FilePath);
    }

    [Fact]
    public async Task Rename_asset_jsonのnameが旧名と違う_補正せずファイルもDBも索引も変えずに止める()
    {
        await AddAssetAsync("old");
        var otherId = await AddAssetAsync("other");
        using (var db = NewDb())
        {
            db.VectorIndex.Add(VecRow(otherId, "other"));
            await db.SaveChangesAsync();
        }
        // assets/old/asset.json を別装置 other のコピーにする
        File.Copy(Path.Combine(_assetsDir, "other", "asset.json"), Path.Combine(_assetsDir, "old", "asset.json"), true);
        var files = SnapshotFiles();
        var dbRows = await SnapshotDbAsync();
        List<(string, string?, string?)> Vec()
        {
            using var d = NewDb();
            return d.VectorIndex.AsNoTracking().OrderBy(v => v.Id).AsEnumerable()
                .Select(v => (v.AssetName ?? "", v.PartNumber, v.FilePath)).ToList()!;
        }
        var vec = Vec();

        var r = await RenameAsync("old", "new");

        Assert.False(r.Success);
        Assert.Contains(r.Errors, e => e.Contains("asset.json の name") && e.Contains("other"));
        Assert.Equal(files, SnapshotFiles());
        Assert.Equal(dbRows, await SnapshotDbAsync());
        Assert.Equal(vec, Vec());
    }

    [Theory]
    [InlineData("dir")]
    [InlineData("jsondir")]
    [InlineData("file")]
    public async Task Rename_新名と大小文字だけ違うものがassets直下にある_何も変えずに止める(string kind)
    {
        await AddAssetAsync("old");
        var path = Path.Combine(_assetsDir, "NEW");
        if (kind == "file") await File.WriteAllTextAsync(path, "x");
        else
        {
            Directory.CreateDirectory(path);
            if (kind == "jsondir") await _json.CreateAsync(path, Guid.NewGuid(), "NEW");
        }
        var files = SnapshotFiles();
        var dbRows = await SnapshotDbAsync();

        var r = await RenameAsync("old", "new");

        Assert.False(r.Success);
        Assert.NotEmpty(r.Errors);
        Assert.Equal(files, SnapshotFiles());
        Assert.Equal(dbRows, await SnapshotDbAsync());
    }

    [Fact]
    public async Task Rename_新名と別の名前のものがあっても_妨げない()
    {
        await AddAssetAsync("old");
        Directory.CreateDirectory(Path.Combine(_assetsDir, "new-x"));
        await File.WriteAllTextAsync(Path.Combine(_assetsDir, "new.txt"), "x");

        var r = await RenameAsync("old", "new");

        Assert.True(r.Success, string.Join(" / ", r.Errors) + r.Failure);
    }

    // ---- Codex 3 周目の指摘への検査 ----

    private async Task SetJsonIdAsync(string name, Guid id)
    {
        var dir = Path.Combine(_assetsDir, name);
        var j = (await _json.ReadAsync(dir))!;
        await _json.WriteAsync(dir, j with { Id = id });
    }

    [Theory]
    [InlineData("db")]
    [InlineData("json")]
    public async Task Rename_asset_jsonのIDが別の装置のもの_何も変えずに止める(string owner)
    {
        await AddAssetAsync("old");
        Guid otherId;
        if (owner == "db")
        {
            otherId = await AddAssetAsync("other");
            Directory.Delete(Path.Combine(_assetsDir, "other"), true); // DB の行だけが所有者
        }
        else
        {
            otherId = await AddAssetAsync("other", inDb: false);        // assets/ の別の asset.json が所有者
        }
        using (var db = NewDb())
        {
            db.VectorIndex.Add(VecRow(otherId, "other"));
            await db.SaveChangesAsync();
        }
        await SetJsonIdAsync("old", otherId); // old の asset.json に other の ID をコピー
        var files = SnapshotFiles();
        var dbRows = await SnapshotDbAsync();

        var r = await RenameAsync("old", "new");

        Assert.False(r.Success);
        Assert.Contains(r.Errors, e => e.Contains("別の装置"));
        Assert.Equal(files, SnapshotFiles());
        Assert.Equal(dbRows, await SnapshotDbAsync());
        using var check = NewDb();
        Assert.Equal("other", (await check.VectorIndex.AsNoTracking().SingleAsync()).AssetName);
    }

    [Fact]
    public async Task Rename_どの装置にも属さない旧版のID食い違いは_通して両方のIDの索引を更新する()
    {
        var jsonId = await AddAssetAsync("old");
        var dbId = Guid.NewGuid();
        using (var db = NewDb())
        {
            // DB の行の ID を asset.json の ID と違うものにする（旧版の登録）
            db.Assets.RemoveRange(db.Assets);
            db.Assets.Add(Asset.Create("old", Path.Combine(_assetsDir, "old"), "old", "", dbId));
            db.VectorIndex.AddRange(VecRow(jsonId, "old"), VecRow(dbId, "old", "spec"));
            await db.SaveChangesAsync();
        }

        var r = await RenameAsync("old", "new");

        Assert.True(r.Success, string.Join(" / ", r.Errors) + r.Failure);
        Assert.Equal(2, r.VectorRowsUpdated);
    }

    [Theory]
    [InlineData("new")]
    [InlineData("NEW")]
    public async Task Rename_新名と同じ名前のアーカイブがある_DBに行が無くても何も変えずに止める(string archivedName)
    {
        await AddAssetAsync("old");
        await WriteArchiveIndexAsync(archivedName);
        var files = SnapshotRoot();
        var dbRows = await SnapshotDbAsync();

        var r = await RenameAsync("old", "new");

        Assert.False(r.Success);
        Assert.Contains(r.Errors, e => e.Contains("同じ名前のアーカイブ"));
        Assert.Equal(files, SnapshotRoot());
        Assert.Equal(dbRows, await SnapshotDbAsync());
    }

    [Fact]
    public async Task Rename_別の名前のアーカイブは_改名を妨げない()
    {
        await AddAssetAsync("old");
        await WriteArchiveIndexAsync("new-x", "other");

        var r = await RenameAsync("old", "new");

        Assert.True(r.Success, string.Join(" / ", r.Errors) + r.Failure);
    }

    [Fact]
    public async Task Rename_asset_jsonに履歴欄が無ければDBの履歴を引き継ぐ_a_b_のあとJSONを作り直してb_c()
    {
        await AddAssetAsync("a");
        Assert.True((await RenameAsync("a", "b")).Success);
        // asset.json を失い、asset add 相当（previous_names の無い asset.json）で作り直す。DB の履歴は [a]
        var dir = Path.Combine(_assetsDir, "b");
        File.Delete(Path.Combine(dir, "asset.json"));
        var dbId = (await SnapshotDbAsync()).Single().Id;
        await _json.CreateAsync(dir, dbId, "b", "b", "");

        Assert.True((await RenameAsync("b", "c")).Success);

        Assert.Equal(new[] { "a", "b" }, (await _json.ReadAsync(Path.Combine(_assetsDir, "c")))!.PreviousNames);
        using var db = NewDb();
        Assert.Equal(new[] { "a", "b" }, (await db.Assets.AsNoTracking().SingleAsync()).PreviousNames);
    }

    [Fact]
    public async Task Rename_asset_jsonの履歴が明示的な空配列ならasset_jsonを正とする()
    {
        await AddAssetAsync("a");
        Assert.True((await RenameAsync("a", "b")).Success);
        var dir = Path.Combine(_assetsDir, "b");
        var j = (await _json.ReadAsync(dir))!;
        await _json.WriteAsync(dir, j with { PreviousNames = new List<string>() }); // DB は [a]、JSON は []

        Assert.True((await RenameAsync("b", "c")).Success);

        Assert.Equal(new[] { "b" }, (await _json.ReadAsync(Path.Combine(_assetsDir, "c")))!.PreviousNames);
    }

    // ---- Codex 4 周目の指摘への検査 ----

    [Fact]
    public async Task Rename_改名してアーカイブし名前を再利用した別装置の同期が_旧装置の文書行を上書きしない()
    {
        // A の文書を、実際の決定論的 ID（SyncCommand.DocChunkId）で索引化する
        var idA = await AddAssetAsync("A");
        var docKey = DesignAid.Commands.SyncCommand.DocChunkId("A", "DESIGN.md", 0);
        using (var db = NewDb())
        {
            var row = VecRow(idA, "A");
            row.PartId = docKey.ToString();
            row.Content = "Aの旧い内容";
            db.VectorIndex.Add(row);
            await db.SaveChangesAsync();
        }

        // A → B に改名（再同期はしない）
        Assert.True((await RenameAsync("A", "B")).Success);
        using (var db = NewDb())
        {
            // B の文書行が、旧名から作られたキーのまま残っていない
            Assert.False(await db.VectorIndex.AnyAsync(v => v.PartId == docKey.ToString()));
        }

        // B をアーカイブした状態で、新しい装置 A を作り、同名の文書を同期する（SyncVectorIndex と同じ UPSERT）
        var newAId = Guid.NewGuid();
        using (var db = NewDb())
        using (var vs = new VectorSearchService(db, new DesignAid.Infrastructure.Embedding.MockEmbeddingProvider(8),
                   Path.Combine(_root, "hnsw.bin")))
        {
            await vs.UpsertPartsAsync(new[]
            {
                new DesignKnowledgePoint
                {
                    Id = docKey, PartId = docKey, PartNumber = "[DOC] A/DESIGN.md", AssetId = newAId, AssetName = "A",
                    Type = "asset_doc", Content = "新しい A の内容", FilePath = "assets/A/DESIGN.md"
                }
            });
        }

        using var check = NewDb();
        var rows = await check.VectorIndex.AsNoTracking().ToListAsync();
        var a = Assert.Single(rows); // 新しい行が 1 つだけ。B の行を上書きしていない
        Assert.Equal(newAId.ToString(), a.AssetId);
        Assert.Equal("新しい A の内容", a.Content);
        Assert.DoesNotContain(rows, v => v.AssetId == idA.ToString());
    }

    [Fact]
    public async Task Rename_部品のspec行は_消さずに装置名だけ更新する()
    {
        var id = await AddAssetAsync("old");
        var partRowId = Guid.NewGuid().ToString();
        using (var db = NewDb())
        {
            var row = VecRow(id, "old", "spec");
            row.PartId = partRowId;
            db.VectorIndex.Add(row);
            await db.SaveChangesAsync();
        }

        Assert.True((await RenameAsync("old", "new")).Success);

        using var check = NewDb();
        var left = await check.VectorIndex.AsNoTracking().SingleAsync();
        Assert.Equal(partRowId, left.PartId);
        Assert.Equal("new", left.AssetName);
    }

    [Fact]
    public async Task Rename_別フォルダのasset_jsonのnameが新名_何も変えずに止める()
    {
        await AddAssetAsync("old");
        await AddAssetAsync("x", inDb: false);
        var xDir = Path.Combine(_assetsDir, "x");
        var xj = (await _json.ReadAsync(xDir))!;
        await _json.WriteAsync(xDir, xj with { Name = "NEW" }); // フォルダ名と JSON 名が違う未同期の装置
        var files = SnapshotFiles();
        var dbRows = await SnapshotDbAsync();

        var r = await RenameAsync("old", "new");

        Assert.False(r.Success);
        Assert.Contains(r.Errors, e => e.Contains("asset.json の name") && e.Contains("x"));
        Assert.Equal(files, SnapshotFiles());
        Assert.Equal(dbRows, await SnapshotDbAsync());
    }

    [Fact]
    public async Task Rename_関係のない名前のasset_jsonは_改名を妨げない()
    {
        await AddAssetAsync("old");
        await AddAssetAsync("x", inDb: false);
        var xDir = Path.Combine(_assetsDir, "x");
        var xj = (await _json.ReadAsync(xDir))!;
        await _json.WriteAsync(xDir, xj with { Name = "new-x" });

        var r = await RenameAsync("old", "new");

        Assert.True(r.Success, string.Join(" / ", r.Errors) + r.Failure);
    }

    [Fact]
    public async Task Rename_別の改名がロックを持っている間は止まり_解放された後は通る()
    {
        await AddAssetAsync("old");
        var files = SnapshotRoot();
        var dbRows = await SnapshotDbAsync();
        var lockPath = Path.Combine(_root, DesignAid.Application.Services.ProjectLock.FileName);

        AssetRenameResult busy;
        using (new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            busy = await RenameAsync("old", "new");
        }

        Assert.False(busy.Success);
        Assert.Contains(busy.Errors, e => e.Contains("別の変更が実行中"));

        File.Delete(lockPath);
        Assert.Equal(files.Where(k => !k.Key.Contains(DesignAid.Application.Services.ProjectLock.FileName)).ToDictionary(k => k.Key, k => k.Value),
            SnapshotRoot().Where(k => !k.Key.Contains(DesignAid.Application.Services.ProjectLock.FileName)).ToDictionary(k => k.Key, k => k.Value));
        Assert.Equal(dbRows, await SnapshotDbAsync());

        var r = await RenameAsync("old", "new");
        Assert.True(r.Success, string.Join(" / ", r.Errors) + r.Failure);
    }

    [Fact]
    public async Task Rename_実行中の改名がある間に別の改名を呼ぶと止まり_終わった後は通る()
    {
        await AddAssetAsync("a");
        await AddAssetAsync("b");
        var reached = new TaskCompletionSource();
        var release = new TaskCompletionSource();

        // 1 つ目: フォルダを移した直後で止めておく（ロックを持ったまま）
        var first = Task.Run(() => RenameAsync("a", "a2", async step =>
        {
            if (step == "after-move")
            {
                reached.SetResult();
                await release.Task;
            }
        }));
        await reached.Task;

        var second = await RenameAsync("b", "b2"); // 2 つ目: 待たずに止まる
        Assert.False(second.Success);
        Assert.Contains(second.Errors, e => e.Contains("別の変更が実行中"));
        Assert.True(Directory.Exists(Path.Combine(_assetsDir, "b"))); // 何も変わっていない

        release.SetResult();
        Assert.True((await first).Success);

        var third = await RenameAsync("b", "b2"); // 終わった後は通る
        Assert.True(third.Success, string.Join(" / ", third.Errors) + third.Failure);
    }

    [Fact]
    public async Task Rename_失敗した後もロックは残らず_続けて改名できる()
    {
        await AddAssetAsync("old");
        var failed = await RenameAsync("old", "new",
            step => step == "after-json" ? throw new InvalidOperationException("注入した失敗") : Task.CompletedTask);
        Assert.False(failed.Success);

        var r = await RenameAsync("old", "new");

        Assert.True(r.Success, string.Join(" / ", r.Errors) + r.Failure);
    }

    // ---- Codex 5 周目の指摘への検査 ----

    [Fact]
    public async Task Rename_asset_jsonのIDがDBのIDと一致していて_別フォルダにもコピーされている_何も変えずに止める()
    {
        var id = await AddAssetAsync("old");
        var otherDir = Path.Combine(_assetsDir, "copy");
        Directory.CreateDirectory(otherDir);
        await _json.CreateAsync(otherDir, id, "copy"); // old の ID をコピーした別フォルダ
        using (var db = NewDb())
        {
            db.VectorIndex.Add(VecRow(id, "old"));
            await db.SaveChangesAsync();
        }
        var files = SnapshotFiles();
        var dbRows = await SnapshotDbAsync();

        var r = await RenameAsync("old", "new");

        Assert.False(r.Success);
        Assert.Contains(r.Errors, e => e.Contains("別の装置") && e.Contains("copy"));
        Assert.Equal(files, SnapshotFiles());
        Assert.Equal(dbRows, await SnapshotDbAsync());
        using var check = NewDb();
        Assert.Equal("old", (await check.VectorIndex.AsNoTracking().SingleAsync()).AssetName);
    }

    [Fact]
    public async Task Rename_DB側のIDが別フォルダのasset_jsonにある_ID食い違いでも何も変えずに止める()
    {
        var jsonId = await AddAssetAsync("old", inDb: false);
        var dbId = Guid.NewGuid();
        using (var db = NewDb())
        {
            db.Assets.Add(Asset.Create("old", Path.Combine(_assetsDir, "old"), "old", "", dbId));
            await db.SaveChangesAsync();
        }
        var otherDir = Path.Combine(_assetsDir, "copy");
        Directory.CreateDirectory(otherDir);
        await _json.CreateAsync(otherDir, dbId, "copy"); // DB 側の ID が別フォルダの asset.json に
        var files = SnapshotFiles();
        var dbRows = await SnapshotDbAsync();

        var r = await RenameAsync("old", "new");

        Assert.False(r.Success);
        Assert.Contains(r.Errors, e => e.Contains("別の装置"));
        Assert.Equal(files, SnapshotFiles());
        Assert.Equal(dbRows, await SnapshotDbAsync());
        Assert.NotEqual(jsonId, dbId);
    }

    [Fact]
    public async Task Rename_IDが一致していて所有者が自分だけなら_通る()
    {
        await AddAssetAsync("old");
        await AddAssetAsync("unrelated"); // 別の ID の別の装置は妨げない

        var r = await RenameAsync("old", "new");

        Assert.True(r.Success, string.Join(" / ", r.Errors) + r.Failure);
    }

    [Fact]
    public async Task Rename_新名のUTF8が255バイトを超える_何も変えずに止め_旧名の長さは制限しない()
    {
        await AddAssetAsync("old");
        var files = SnapshotFiles();

        var tooLong = await RenameAsync("old", new string('漢', 86)); // 258 バイト

        Assert.False(tooLong.Success);
        Assert.Equal(files, SnapshotFiles());

        // 旧名が 255 バイトを超えていても（既存の長い名前）、改名できる
        var longOld = new string('漢', 86);
        await AddAssetAsync(longOld);
        var r = await RenameAsync(longOld, "short");
        Assert.True(r.Success, string.Join(" / ", r.Errors) + r.Failure);
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
