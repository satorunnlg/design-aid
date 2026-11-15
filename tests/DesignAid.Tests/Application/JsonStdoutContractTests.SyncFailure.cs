using System.Text.Json;
using DesignAid.Application.Services;
using DesignAid.Commands;
using DesignAid.Domain.Entities;
using DesignAid.Infrastructure.FileSystem;

namespace DesignAid.Tests.Application;

// sync の各段階の失敗が、--json の有無にかかわらず終了コードに届くこと、復元の個別の失敗が結果に入ること、
// DB 同期が途中で失敗したときの件数が DB の実態と一致することの検査（#10）
public partial class JsonStdoutContractTests
{
    /// <summary>プロジェクトを作って <paramref name="mutate"/> で状態を変え、2 つにコピーして、sync と sync --json を動かす。</summary>
    private async Task<((int Code, string Out, string Err) Plain, (int Code, string Out, string Err) Json, string JsonRoot)> RunSyncBothAsync(
        Func<string, Task> mutate, params string[] extra)
    {
        var template = await CreateHealthyProjectAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        await mutate(template);
        Directory.SetCurrentDirectory(_base);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        var plain = Path.Combine(_base, "plain-" + Guid.NewGuid().ToString("N")[..6]);
        var json = Path.Combine(_base, "json-" + Guid.NewGuid().ToString("N")[..6]);
        CopyDir(template, plain);
        CopyDir(template, json);

        Directory.SetCurrentDirectory(plain);
        var r1 = await RunAsync(new[] { "sync" }.Concat(extra).ToArray());
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.SetCurrentDirectory(json);
        var r2 = await RunAsync(new[] { "sync", "--json" }.Concat(extra).ToArray());
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.SetCurrentDirectory(_base);
        return (r1, r2, json);
    }

    private async Task<(int Code, string Out, string Err)> RunIn(string root, params string[] args)
    {
        Directory.SetCurrentDirectory(root);
        var r = await RunAsync(args);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.SetCurrentDirectory(_base);
        return r;
    }

    private static Task SetConfigAsync(string root, string key, string value)
    {
        // config set は DB の設定表へ書く。ここでは直接 SQL で書く（コマンドの検査ではないので）
        using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Path.Combine(root, "design_aid.db")};Pooling=False");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name LIKE '%Setting%'";
        var table = (string?)cmd.ExecuteScalar() ?? throw new InvalidOperationException("設定表が無い");
        cmd.CommandText = $"PRAGMA table_info(\"{table}\")";
        var cols = new List<string>();
        using (var r = cmd.ExecuteReader()) while (r.Read()) cols.Add(r.GetString(1));
        var keyCol = cols.First(c => c.Equals("Key", StringComparison.OrdinalIgnoreCase));
        var valCol = cols.First(c => c.Equals("Value", StringComparison.OrdinalIgnoreCase));
        cmd.CommandText = $"DELETE FROM \"{table}\" WHERE \"{keyCol}\" = $k";
        cmd.Parameters.AddWithValue("$k", key);
        cmd.ExecuteNonQuery();
        var extraCols = cols.Where(c => c != keyCol && c != valCol).ToList();
        var colList = string.Join(",", new[] { keyCol, valCol }.Concat(extraCols).Select(c => $"\"{c}\""));
        var vals = string.Join(",", new[] { "$k", "$v" }.Concat(extraCols.Select(c => c.EndsWith("At") ? "'2026-01-01 00:00:00'" : "''")));
        cmd.CommandText = $"INSERT INTO \"{table}\" ({colList}) VALUES ({vals})";
        cmd.Parameters.AddWithValue("$v", value);
        cmd.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    private static string ErrText((int Code, string Out, string Err) r) => $"code={r.Code} out=<{r.Out.Trim()}> err=<{r.Err.Trim()}>";

    /// <summary>失敗する段階ごとに、--json の有無の両方で終了コードが 0 以外になり、JSON の success が偽になる。</summary>
    [Fact]
    public async Task sync_DB同期が失敗_jsonの有無にかかわらず終了コードは0以外_jsonはsuccess偽()
    {
        var r = await RunSyncBothAsync(root =>
        {
            File.WriteAllText(Path.Combine(root, "design_aid.db"), "これは SQLite ではない");
            return Task.CompletedTask;
        });
        Assert.NotEqual(0, r.Plain.Code);
        Assert.NotEqual(0, r.Json.Code);
        Assert.True(JsonGuard.IsSingleJson(r.Json.Out), ErrText(r.Json));
        using var doc = JsonDocument.Parse(r.Json.Out);
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task sync_ベクトル同期が失敗_jsonの有無にかかわらず終了コードは0以外_jsonはsuccess偽()
    {
        var r = await RunSyncBothAsync(root => SetConfigAsync(root, "embedding.provider", "存在しないプロバイダー"), "--include-vectors");
        Assert.NotEqual(0, r.Plain.Code);
        Assert.NotEqual(0, r.Json.Code);
        using var doc = JsonDocument.Parse(r.Json.Out);
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("error", doc.RootElement.GetProperty("vectorIndex").GetProperty("status").GetString());
    }

    /// <summary>正常なスキップ（ベクトル検索の無効・同期対象なし・dry-run）と、警告だけの場合は、失敗にしない。</summary>
    [Fact]
    public async Task sync_正常なスキップと警告は失敗にしない_終了コード0_jsonはsuccess真()
    {
        // disabled
        var d = await RunSyncBothAsync(root => SetConfigAsync(root, "vector_search.enabled", "false"), "--include-vectors");
        Assert.Equal(0, d.Plain.Code); Assert.Equal(0, d.Json.Code);
        using (var doc = JsonDocument.Parse(d.Json.Out))
        {
            Assert.True(doc.RootElement.GetProperty("success").GetBoolean(), ErrText(d.Json));
            Assert.Equal("disabled", doc.RootElement.GetProperty("vectorIndex").GetProperty("status").GetString());
        }
        // dry-run
        var y = await RunSyncBothAsync(_ => Task.CompletedTask, "--dry-run", "--include-vectors");
        Assert.Equal(0, y.Plain.Code); Assert.Equal(0, y.Json.Code);
        using (var doc = JsonDocument.Parse(y.Json.Out))
        {
            Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
            Assert.Equal("skipped", doc.RootElement.GetProperty("vectorIndex").GetProperty("status").GetString());
        }
        // 同期対象なし（パーツも文書も無いプロジェクトで、ベクトル同期）
        var n = await RunSyncBothAsync(root =>
        {
            Directory.Delete(Path.Combine(root, "components", "P-1"), recursive: true);
            return Task.CompletedTask;
        }, "--include-vectors");
        Assert.Equal(0, n.Plain.Code); Assert.Equal(0, n.Json.Code);
        using (var doc = JsonDocument.Parse(n.Json.Out))
            Assert.True(doc.RootElement.GetProperty("success").GetBoolean(), ErrText(n.Json));
        // 警告だけ: asset.json が欠けた装置を、--restore-json なしで報告するだけ
        var w = await RunSyncBothAsync(root => { File.Delete(Path.Combine(root, "assets", "b", "asset.json")); return Task.CompletedTask; });
        Assert.Equal(0, w.Plain.Code); Assert.Equal(0, w.Json.Code);
        using (var doc = JsonDocument.Parse(w.Json.Out))
        {
            Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
            Assert.Equal(1, doc.RootElement.GetProperty("assetJsonGaps").GetArrayLength());
        }
    }

    // ---- 復元の個別の失敗 ----

    private static Task BlockAssetJson(string root, params string[] names)
    {
        foreach (var n in names)
        {
            var dir = Path.Combine(root, "assets", n);
            Directory.CreateDirectory(dir);
            var f = Path.Combine(dir, "asset.json");
            if (File.Exists(f)) File.Delete(f);
            Directory.CreateDirectory(f); // asset.json の場所に同名のディレクトリがあり、書き込みに失敗する
        }
        return Task.CompletedTask;
    }

    [Fact]
    public async Task sync_復元が全件失敗_終了コードは0以外_失敗が結果に入る_jsonの有無の両方()
    {
        var r = await RunSyncBothAsync(root => BlockAssetJson(root, "b"), "--restore-json");
        Assert.NotEqual(0, r.Plain.Code);
        Assert.NotEqual(0, r.Json.Code);
        using var doc = JsonDocument.Parse(r.Json.Out);
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean(), ErrText(r.Json));
        Assert.Equal(1, doc.RootElement.GetProperty("assetJsonRestoreErrors").GetArrayLength());
        Assert.Equal(0, doc.RootElement.GetProperty("assetJsonRestored").GetInt32());
    }

    [Fact]
    public async Task sync_復元が一部失敗_成功した分は復元され_失敗は結果に入り_終了コードは0以外()
    {
        // 装置 b・c の asset.json が欠け、b だけ書き込みを妨げる。c は復元される
        var r = await RunSyncBothAsync(async root =>
        {
            var (c, so, se) = await RunIn(root, "asset", "add", "c", "--no-git");
            Assert.True(c == 0, so + se);
            File.Delete(Path.Combine(root, "assets", "c", "asset.json"));
            await BlockAssetJson(root, "b");
        }, "--restore-json");
        Assert.NotEqual(0, r.Plain.Code);
        Assert.NotEqual(0, r.Json.Code);
        using var doc = JsonDocument.Parse(r.Json.Out);
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(1, doc.RootElement.GetProperty("assetJsonRestored").GetInt32());
        Assert.Equal(1, doc.RootElement.GetProperty("assetJsonRestoreErrors").GetArrayLength());
        Assert.True(File.Exists(Path.Combine(r.JsonRoot, "assets", "c", "asset.json")));
    }

    [Fact]
    public async Task RestoreAssetJson_検出後に作られたファイルは触らず_失敗にも数えない()
    {
        var root = await CreateHealthyProjectAsync();
        var dir = Path.Combine(root, "assets", "b");
        var gap = new AssetJsonGap(Guid.NewGuid(), "b", dir, "b", "", AssetStatus.Active, Array.Empty<string>(), DateTime.UtcNow);
        File.WriteAllText(Path.Combine(dir, "asset.json"), "{\"id\":\"x\",\"name\":\"b\"}");
        var errors = new List<string>();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        using var db = CommandHelper.CreateDbContext();
        var restored = new SyncService(db, new HashService()).RestoreAssetJson(new[] { gap }, (n, e) => errors.Add(n));
        Assert.Equal(0, restored);
        Assert.Empty(errors);
        Assert.Equal("{\"id\":\"x\",\"name\":\"b\"}", File.ReadAllText(Path.Combine(dir, "asset.json")));
    }

    // ---- DB 同期が途中で失敗したときの件数 ----

    private static int CountRows(string root, string table)
    {
        using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Path.Combine(root, "design_aid.db")};Pooling=False");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM \"{table}\"";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    [Fact]
    public async Task sync_DB同期が装置の登録のあとで失敗_確定した装置の件数を保ち_DBの実態と一致する()
    {
        // 装置 z は登録される（段階 1 の SaveChanges が済む）。同じ型式のパーツ P-3 が 2 つあり、段階 2 で失敗する
        var r = await RunSyncBothAsync(root =>
        {
            var aJson = File.ReadAllText(Path.Combine(root, "assets", "a", "asset.json"));
            var aId = JsonDocument.Parse(aJson).RootElement.GetProperty("id").GetString()!;
            Directory.CreateDirectory(Path.Combine(root, "assets", "z"));
            File.WriteAllText(Path.Combine(root, "assets", "z", "asset.json"),
                aJson.Replace(aId, Guid.NewGuid().ToString()).Replace("\"name\": \"a\"", "\"name\": \"z\""));
            var pJson = File.ReadAllText(Path.Combine(root, "components", "P-1", "part.json"));
            var pId = JsonDocument.Parse(pJson).RootElement.GetProperty("id").GetString()!;
            // 同じ型式のパーツが 2 つのディレクトリにあり、段階 2 の SaveChanges が一意制約で失敗する
            foreach (var n in new[] { "P-3a", "P-3b" })
            {
                Directory.CreateDirectory(Path.Combine(root, "components", n));
                File.WriteAllText(Path.Combine(root, "components", n, "part.json"),
                    pJson.Replace(pId, Guid.NewGuid().ToString()).Replace("P-1", "P-3"));
            }
            return Task.CompletedTask;
        });
        Assert.NotEqual(0, r.Json.Code);
        using var doc = JsonDocument.Parse(r.Json.Out);
        var db = doc.RootElement.GetProperty("database");
        Assert.False(string.IsNullOrEmpty(db.GetProperty("error").GetString()), r.Json.Out);
        Assert.Equal(3, CountRows(r.JsonRoot, "Assets")); // a・b・z
        Assert.Equal(1, db.GetProperty("assets").GetInt32());
        Assert.Equal(0, db.GetProperty("uncommitted").GetProperty("assets").GetInt32());
        Assert.Equal(1, CountRows(r.JsonRoot, "Parts")); // P-1 のみ（P-3 は入っていない）
        Assert.Equal(0, db.GetProperty("parts").GetInt32());
        Assert.Equal(2, db.GetProperty("uncommitted").GetProperty("parts").GetInt32());
    }

    [Fact]
    public async Task sync_DB同期が装置の登録の保存で失敗_確定は0_未確定の件数を分けて報告し_DBに入っていない()
    {
        // 同じ名前の asset.json が 2 つのディレクトリ（z1・z2）にあると、段階 1 の SaveChanges が一意制約で失敗する
        var r = await RunSyncBothAsync(root =>
        {
            var aJson = File.ReadAllText(Path.Combine(root, "assets", "a", "asset.json"));
            var aId = JsonDocument.Parse(aJson).RootElement.GetProperty("id").GetString()!;
            foreach (var n in new[] { "z1", "z2" })
            {
                Directory.CreateDirectory(Path.Combine(root, "assets", n));
                File.WriteAllText(Path.Combine(root, "assets", n, "asset.json"),
                    aJson.Replace(aId, Guid.NewGuid().ToString()).Replace("\"name\": \"a\"", "\"name\": \"z\""));
            }
            return Task.CompletedTask;
        });
        Assert.NotEqual(0, r.Json.Code);
        using var doc = JsonDocument.Parse(r.Json.Out);
        var db = doc.RootElement.GetProperty("database");
        Assert.False(string.IsNullOrEmpty(db.GetProperty("error").GetString()), r.Json.Out);
        Assert.Equal(2, CountRows(r.JsonRoot, "Assets")); // a・b のまま
        Assert.Equal(0, db.GetProperty("assets").GetInt32());
        Assert.Equal(2, db.GetProperty("uncommitted").GetProperty("assets").GetInt32());
    }
}
