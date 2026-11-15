using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.Text.Json;
using DesignAid.Application.Services;
using DesignAid.Commands;
using DesignAid.Commands.Asset;
using DesignAid.Domain.Entities;
using DesignAid.Infrastructure.Embedding;
using DesignAid.Infrastructure.FileSystem;
using DesignAid.Infrastructure.Persistence;
using DesignAid.Infrastructure.VectorSearch;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DesignAid.Tests.Application;

/// <summary>環境変数・カレントディレクトリ・Console を差し替えるテストは、並列に走らせない。</summary>
[CollectionDefinition("CommandEnv", DisableParallelization = true)]
public class CommandEnvCollection { }

/// <summary>
/// プロジェクト共通ロック（ProjectLock）と、それに参加する入口、改名後の検索キャッシュ、
/// --json の早期の失敗のテスト。実データの DB・assets/ には触れない（すべて一時ディレクトリ）。
/// </summary>
[Collection("CommandEnv")]
public class ProjectLockTests : IDisposable
{
    private readonly string _root;
    private readonly string _assetsDir;
    private readonly string _dbPath;
    private readonly AssetJsonReader _json = new();
    private readonly string? _savedEnv = Environment.GetEnvironmentVariable("DA_DATA_DIR");
    private readonly string _savedCwd = Directory.GetCurrentDirectory();

    public ProjectLockTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "design-aid-lock-tests", Guid.NewGuid().ToString());
        _assetsDir = Path.Combine(_root, "assets");
        _dbPath = Path.Combine(_root, "design_aid.db");
        Directory.CreateDirectory(_assetsDir);
        Directory.CreateDirectory(Path.Combine(_root, "components"));
        using var db = NewDb();
        db.Database.Migrate(); // コマンドが使う CreateDbContext と同じ、マイグレーション済みの DB
        Environment.SetEnvironmentVariable("DA_DATA_DIR", _root);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("DA_DATA_DIR", _savedEnv);
        Directory.SetCurrentDirectory(_savedCwd);
        SqliteConnection.ClearAllPools();
        try { CommandHelperForTests.ForceDelete(_root); } catch { }
        GC.SuppressFinalize(this);
    }

    private DesignAidDbContext NewDb() => new(new DbContextOptionsBuilder<DesignAidDbContext>()
        .UseSqlite($"Data Source={_dbPath}").Options);

    private async Task<Guid> AddAssetAsync(string name, bool inDb = true)
    {
        var id = Guid.NewGuid();
        var dir = Path.Combine(_assetsDir, name);
        Directory.CreateDirectory(dir);
        await _json.CreateAsync(dir, id, name, name, "説明");
        if (inDb)
        {
            using var db = NewDb();
            db.Assets.Add(Asset.Create(name, dir, name, "説明", id));
            await db.SaveChangesAsync();
        }
        return id;
    }

    /// <summary>コマンドを実行し、(終了コード, 標準出力, 標準エラー) を返す。</summary>
    private static async Task<(int Code, string Out, string Err)> RunAsync(Command command, params string[] args)
    {
        var so = Console.Out; var se = Console.Error;
        var outW = new StringWriter(); var errW = new StringWriter();
        Console.SetOut(outW); Console.SetError(errW);
        Environment.ExitCode = 0;
        try
        {
            var code = await command.InvokeAsync(args);
            return (code, outW.ToString(), errW.ToString());
        }
        finally
        {
            Console.SetOut(so); Console.SetError(se);
        }
    }

    // ---- ProjectLock 本体 ----

    [Fact]
    public async Task ProjectLock_別の流れが持っている間は取れず_再入は自己競合せず_解放後は取れて_ファイルも残らない()
    {
        var acquired = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var holder = Task.Run(async () =>
        {
            using var h = ProjectLock.TryAcquire(_root);
            Assert.NotNull(h);
            // 同じ流れでの二重取得は、自分自身とぶつからない（何もしないハンドル）
            using var inner = ProjectLock.TryAcquire(_root);
            Assert.NotNull(inner);
            Assert.True(inner!.IsReentrant);
            acquired.SetResult();
            await release.Task;
        });
        await acquired.Task;

        Assert.Null(ProjectLock.TryAcquire(_root)); // 別の流れ: 待たずに取れない

        release.SetResult();
        await holder;

        using (var again = ProjectLock.TryAcquire(_root))
        {
            Assert.NotNull(again);
        }
        Assert.False(File.Exists(Path.Combine(_root, ProjectLock.FileName)));
    }

    [Fact]
    public void ProjectLock_共有違反とロック違反だけがbusy_容量不足や場所が無いは別の原因()
    {
        Assert.True(ProjectLock.IsBusy(new IOException("x", unchecked((int)0x80070020)))); // ERROR_SHARING_VIOLATION
        Assert.True(ProjectLock.IsBusy(new IOException("x", unchecked((int)0x80070021)))); // ERROR_LOCK_VIOLATION
        Assert.True(ProjectLock.IsBusy(new IOException("x", 11)));                          // Linux: EWOULDBLOCK
        Assert.True(ProjectLock.IsBusy(new IOException("x", 35)));                          // macOS: EWOULDBLOCK
        Assert.False(ProjectLock.IsBusy(new IOException("disk full", unchecked((int)0x80070070)))); // ERROR_DISK_FULL
        Assert.False(ProjectLock.IsBusy(new IOException("io", 5)));                         // EIO
        Assert.False(ProjectLock.IsBusy(new IOException("enospc", 28)));                    // ENOSPC
        Assert.False(ProjectLock.IsBusy(new DirectoryNotFoundException("x")));
        Assert.False(ProjectLock.IsBusy(new FileNotFoundException("x")));
        Assert.False(ProjectLock.IsBusy(new PathTooLongException("x")));
    }

    [Fact]
    public void ProjectLock_場所が無いなどのIOエラーはbusyにせず元の例外で投げる()
    {
        var missing = Path.Combine(_root, "no-such-dir");
        Assert.Throws<DirectoryNotFoundException>(() => ProjectLock.TryAcquire(missing));
    }

    [Fact]
    public async Task Rename_プロジェクトの場所が無い_busyではなく元の例外のまま出る()
    {
        var fake = Path.Combine(_root, "no-such-dir", "assets");
        using var db = NewDb();
        await Assert.ThrowsAsync<DirectoryNotFoundException>(
            () => new AssetRenameService(db).RenameAsync(fake, "a", "b"));
    }

    // ---- 入口の参加（改名の途中で他の入口を呼ぶ）----

    [Fact]
    public async Task 改名の途中では_sync_update_add_remove_linkが待たずに止まり何も変えず_終われば通る()
    {
        await AddAssetAsync("old");
        await AddAssetAsync("other");
        var reached = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var dbRowsBefore = NewDb().Assets.AsNoTracking().Count();

        var rename = Task.Run(async () =>
        {
            using var db = NewDb();
            return await new AssetRenameService(db)
            {
                StepHook = async step =>
                {
                    if (step == "after-json") { reached.SetResult(); await release.Task; }
                }
            }.RenameAsync(_assetsDir, "old", "new");
        });
        await reached.Task; // asset.json は新名、DB はまだ旧名の状態

        var jsonBefore = await File.ReadAllTextAsync(Path.Combine(_assetsDir, "new", "asset.json"));
        var blocked = new (string Name, Command Cmd, string[] Args)[]
        {
            ("sync", new DesignAid.Commands.SyncCommand(), Array.Empty<string>()),
            ("asset update", new AssetUpdateCommand(), new[] { "other", "--description", "変えない" }),
            ("asset add", new AssetAddCommand(), new[] { "added" }),
            ("asset remove", new AssetRemoveCommand(), new[] { "other", "--force" }),
            ("asset link", new AssetLinkCommand(), new[] { "other", "--child", "new" }),
            ("asset unlink", new AssetUnlinkCommand(), new[] { "other", "--child", "new" }),
        };
        foreach (var (name, cmd, args) in blocked)
        {
            var (_, _, err) = await RunAsync(cmd, args);
            Assert.True(err.Contains("別の変更が実行中"), $"{name}: {err}");
        }

        // 何も変わっていない: 余分な行・変更・消えたフォルダが無い
        Assert.Equal(dbRowsBefore, NewDb().Assets.AsNoTracking().Count());
        Assert.False(Directory.Exists(Path.Combine(_assetsDir, "added")));
        Assert.True(Directory.Exists(Path.Combine(_assetsDir, "other")));
        Assert.DoesNotContain("変えない", await File.ReadAllTextAsync(Path.Combine(_assetsDir, "other", "asset.json")));
        Assert.Equal(jsonBefore, await File.ReadAllTextAsync(Path.Combine(_assetsDir, "new", "asset.json")));
        Assert.Empty(NewDb().Assets.AsNoTracking().Where(a => a.Name == "new"));

        release.SetResult();
        Assert.True((await rename).Success);

        // 改名が終われば通る
        var (code, stdout, _) = await RunAsync(new AssetUpdateCommand(), "new", "--description", "通る");
        Assert.Equal(0, code);
        Assert.Contains("Asset updated", stdout);
        var (_, syncOut, syncErr) = await RunAsync(new DesignAid.Commands.SyncCommand());
        Assert.DoesNotContain("別の変更が実行中", syncErr);
        using var check = NewDb();
        Assert.Single(check.Assets.AsNoTracking().Where(a => a.Name == "new")); // sync が別 ID の行を作っていない
        Assert.Equal(2, check.Assets.AsNoTracking().Count());
    }

    [Fact]
    public async Task 改名の途中でも_読み取りだけのlistは止まらない()
    {
        await AddAssetAsync("old");
        var reached = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var rename = Task.Run(async () =>
        {
            using var db = NewDb();
            return await new AssetRenameService(db)
            {
                StepHook = async step => { if (step == "after-move") { reached.SetResult(); await release.Task; } }
            }.RenameAsync(_assetsDir, "old", "new");
        });
        await reached.Task;

        var (_, _, err) = await RunAsync(new AssetListCommand());
        Assert.DoesNotContain("別の変更が実行中", err);

        release.SetResult();
        Assert.True((await rename).Success);
    }

    // ---- 検索キャッシュ ----

    private static VectorIndexEntry Row(Guid assetId, string name, string type, string partId) => new()
    {
        PartId = partId, PartNumber = $"[DOC] {name}/DESIGN.md", Content = name,
        Embedding = new byte[] { 1 }, Dimensions = 1, AssetId = assetId.ToString(), AssetName = name, Type = type,
        FilePath = $"assets/{name}/DESIGN.md", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
    };

    private async Task<(VectorSearchService Vs, DesignAidDbContext Db, string Cache, Guid IdOld)> SetUpIndexedAsync()
    {
        var idOld = await AddAssetAsync("old");
        var idOther = await AddAssetAsync("other");
        var cache = Path.Combine(_root, "hnsw_index.bin");
        var db = NewDb();
        var vs = new VectorSearchService(db, new MockEmbeddingProvider(8), cache);
        await vs.UpsertPartsAsync(new[]
        {
            new DesignKnowledgePoint { Id = Guid.NewGuid(), PartId = Guid.NewGuid(), PartNumber = "[DOC] old/DESIGN.md",
                AssetId = idOld, AssetName = "old", Type = "asset_doc", Content = "旧装置の文書", FilePath = "assets/old/DESIGN.md" },
            new DesignKnowledgePoint { Id = Guid.NewGuid(), PartId = Guid.NewGuid(), PartNumber = "[DOC] other/DESIGN.md",
                AssetId = idOther, AssetName = "other", Type = "asset_doc", Content = "別の装置の文書", FilePath = "assets/other/DESIGN.md" },
        });
        await vs.RebuildIndexAsync();
        var first = await vs.SearchAsync("旧装置の文書", threshold: -1, limit: 10); // 起動中のインスタンスがメモリに読み込む
        Assert.Equal(2, first.Count);
        Assert.True(File.Exists(cache));
        return (vs, db, cache, idOld);
    }

    [Fact]
    public async Task Rename_検索済みのインスタンスが_別のコンテキストの改名の後でも_再同期なしで残りの文書を検索できる()
    {
        var (vs, db, cache, _) = await SetUpIndexedAsync();
        using (vs) using (db)
        {
            using var other = NewDb();
            var r = await new AssetRenameService(other) { HnswIndexPath = cache }.RenameAsync(_assetsDir, "old", "new");
            Assert.True(r.Success, string.Join(" / ", r.Errors) + r.Failure);
            Assert.False(File.Exists(cache)); // ディスクのキャッシュも消える

            var after = await vs.SearchAsync("旧装置の文書", threshold: -1, limit: 10);

            var only = Assert.Single(after); // 消えた文書は出ず、残っている別の装置の文書は欠けない
            Assert.Equal("other", only.AssetName);
        }
    }

    [Fact]
    public async Task Rename_ロールバックしたときは_キャッシュを消さず索引も変わらない()
    {
        var (vs, db, cache, _) = await SetUpIndexedAsync();
        using (vs) using (db)
        {
            using var other = NewDb();
            var r = await new AssetRenameService(other)
            {
                HnswIndexPath = cache,
                StepHook = step => step == "after-db-save" ? throw new InvalidOperationException("注入した失敗") : Task.CompletedTask
            }.RenameAsync(_assetsDir, "old", "new");

            Assert.False(r.Success);
            Assert.True(File.Exists(cache));
            Assert.Equal(2, (await vs.SearchAsync("旧装置の文書", threshold: -1, limit: 10)).Count);
        }
    }

    // ---- --json の早期の失敗 ----

    private static void AssertSingleFailureJson(string stdout)
    {
        using var doc = JsonDocument.Parse(stdout); // 標準出力は有効な JSON 1 つだけ
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.True(doc.RootElement.GetProperty("errors").GetArrayLength() > 0);
    }

    [Fact]
    public async Task Rename_json_プロジェクトが見つからない_標準出力に失敗のJSONを1つだけ出す()
    {
        var empty = Path.Combine(Path.GetTempPath(), "design-aid-lock-tests", Guid.NewGuid().ToString(), "x");
        Directory.CreateDirectory(empty);
        Environment.SetEnvironmentVariable("DA_DATA_DIR", null);
        Directory.SetCurrentDirectory(empty);

        var (code, stdout, stderr) = await RunAsync(new AssetRenameCommand(), "a", "b", "--json");

        Assert.Equal(3, code);
        AssertSingleFailureJson(stdout);
        Assert.Contains("プロジェクトが見つかりません", stderr);
        Directory.SetCurrentDirectory(_savedCwd);
        Directory.Delete(Path.GetDirectoryName(empty)!, true);
    }

    [Fact]
    public async Task Rename_json_初期化の失敗_archive_indexが壊れている_失敗のJSONを1つだけ出し通常モードと終了コードが同じ()
    {
        await AddAssetAsync("old");
        await File.WriteAllTextAsync(Path.Combine(_root, "archive_index.json"), "{ broken");

        var (jsonCode, jsonOut, jsonErr) = await RunAsync(new AssetRenameCommand(), "old", "new", "--json");
        var (plainCode, plainOut, plainErr) = await RunAsync(new AssetRenameCommand(), "old", "new");

        Assert.Equal(1, jsonCode);
        AssertSingleFailureJson(jsonOut);
        Assert.NotEmpty(jsonErr);
        Assert.Equal(1, plainCode); // 通常モードと終了コードは同じ
        Assert.Equal("", plainOut);
        Assert.NotEmpty(plainErr);
        Assert.True(Directory.Exists(Path.Combine(_assetsDir, "old"))); // 何も変わっていない
    }

    // ---- asset add の名前の上限 ----

    [Fact]
    public async Task AssetAdd_新名のUTF8が255バイトを超える名前は拒否し_255バイトちょうどは通る()
    {
        var tooLong = new string('漢', 86); // 258 バイト
        var (code, _, err) = await RunAsync(new AssetAddCommand(), tooLong);
        Assert.Equal(2, code);
        Assert.Contains("装置名が使えません", err);
        Assert.False(Directory.Exists(Path.Combine(_assetsDir, tooLong)));

        var ok = new string('漢', 85); // 255 バイト
        var (okCode, _, okErr) = await RunAsync(new AssetAddCommand(), ok, "--no-git");
        Assert.Equal(0, okCode);
        Assert.DoesNotContain("ERROR", okErr);
        Assert.True(Directory.Exists(Path.Combine(_assetsDir, ok)));
    }
}
