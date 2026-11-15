using System.Text.Json;
using DesignAid.Commands;

namespace DesignAid.Tests.Application;

// sync は --json の有無で行う処理が同じ（違うのは出力の形だけ）ことの検査（#10）
public partial class JsonStdoutContractTests
{
    /// <summary>
    /// 同期の対象がそろった状態のプロジェクトを作る: 装置 a・b（a が b を子に持つ）、パーツ P-1（未登録のファイルあり）に加え、
    /// ディスクにだけある装置 z とパーツ P-2（DB に無い）、asset.json が消えた b（DB だけにある）。
    /// </summary>
    private async Task<string> CreateSyncTargetProjectAsync()
    {
        var root = await CreateHealthyProjectAsync();
        File.WriteAllText(Path.Combine(root, "components", "P-1", "drawing.txt"), "図面");
        var aJson = File.ReadAllText(Path.Combine(root, "assets", "a", "asset.json"));
        var aId = JsonDocument.Parse(aJson).RootElement.GetProperty("id").GetString()!;
        Directory.CreateDirectory(Path.Combine(root, "assets", "z"));
        File.WriteAllText(Path.Combine(root, "assets", "z", "asset.json"),
            aJson.Replace(aId, Guid.NewGuid().ToString()).Replace("\"name\": \"a\"", "\"name\": \"z\""));
        var pJson = File.ReadAllText(Path.Combine(root, "components", "P-1", "part.json"));
        var pId = JsonDocument.Parse(pJson).RootElement.GetProperty("id").GetString()!;
        Directory.CreateDirectory(Path.Combine(root, "components", "P-2"));
        File.WriteAllText(Path.Combine(root, "components", "P-2", "part.json"),
            pJson.Replace(pId, Guid.NewGuid().ToString()).Replace("P-1", "P-2"));
        File.Delete(Path.Combine(root, "assets", "b", "asset.json"));
        Directory.SetCurrentDirectory(_base);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        return root;
    }

    private static void CopyDir(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var f in Directory.GetFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)));
        foreach (var d in Directory.GetDirectories(from)) CopyDir(d, Path.Combine(to, Path.GetFileName(d)));
    }

    /// <summary>
    /// プロジェクトの状態の写し: DB の全テーブルの行（パス・時刻の列を除く。並べ替える）と、assets/・components/ の全ファイルの内容。
    /// </summary>
    private static List<string> Snapshot(string root)
    {
        var lines = new List<string>();
        using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Path.Combine(root, "design_aid.db")};Pooling=False"))
        {
            conn.Open();
            var tables = new List<string>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name";
                using var r = cmd.ExecuteReader();
                while (r.Read()) { var n = r.GetString(0); if (!n.StartsWith("__EF")) tables.Add(n); }
            }
            foreach (var t in tables)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"SELECT * FROM \"{t}\"";
                using var r = cmd.ExecuteReader();
                var rows = new List<string>();
                while (r.Read())
                {
                    var cols = new List<string>();
                    for (var i = 0; i < r.FieldCount; i++)
                    {
                        var n = r.GetName(i);
                        if (n.Contains("Path") || n.EndsWith("At")) continue;
                        // DB の行の Id は同期のたびに新しく振られる（実行ごとに違う）ので、GUID は伏せて比べる（行数・外部キーの有無・他の列は比べる）
                        var v = r.IsDBNull(i) ? "NULL" : System.Text.RegularExpressions.Regex.Replace(
                            Convert.ToString(r.GetValue(i)) ?? "", "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}", "<guid>");
                        cols.Add($"{n}={v}");
                    }
                    rows.Add(string.Join("|", cols));
                }
                rows.Sort(StringComparer.Ordinal);
                lines.Add($"[{t}] {rows.Count}");
                lines.AddRange(rows.Select(x => "  " + x));
            }
        }
        foreach (var sub in new[] { "assets", "components" })
        {
            var dir = Path.Combine(root, sub);
            if (!Directory.Exists(dir)) { lines.Add($"(no {sub})"); continue; }
            foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal))
                lines.Add($"{Path.GetRelativePath(root, f).Replace('\\', '/')}: {File.ReadAllText(f).Replace("\r\n", "\n")}");
        }
        return lines;
    }

    [Theory]
    [InlineData(false, new string[0])]
    [InlineData(false, new[] { "--dry-run" })]
    [InlineData(false, new[] { "--restore-json" })]
    [InlineData(false, new[] { "--restore-json", "--dry-run" })]
    [InlineData(false, new[] { "--force", "--restore-json" })]
    [InlineData(false, new[] { "--skip-db", "--restore-json" })]
    [InlineData(true, new string[0])]
    [InlineData(true, new[] { "--restore-json" })]
    [InlineData(true, new[] { "--restore-json", "--dry-run" })]
    public async Task sync_jsonの有無で_DBとファイルの結果が同じになる(bool removeComponents, string[] extra)
    {
        var template = await CreateSyncTargetProjectAsync();
        if (removeComponents) Directory.Delete(Path.Combine(template, "components"), recursive: true);
        var plain = Path.Combine(_base, "plain");
        var json = Path.Combine(_base, "json");
        CopyDir(template, plain);
        CopyDir(template, json);

        Directory.SetCurrentDirectory(plain);
        var (c1, _, _) = await RunAsync(new[] { "sync" }.Concat(extra).ToArray());
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.SetCurrentDirectory(json);
        var (c2, so2, se2) = await RunAsync(new[] { "sync", "--json" }.Concat(extra).ToArray());
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.SetCurrentDirectory(_base);

        Assert.Equal(0, c1);
        Assert.True(c2 == 0 && JsonGuard.IsSingleJson(so2), Describe(new[] { "sync", "--json" }, c2, so2, se2));
        var s1 = Snapshot(plain);
        var s2 = Snapshot(json);
        Assert.Equal(string.Join("\n", s1), string.Join("\n", s2));

        // 検査に検出力があること（両方が何もしていないだけで一致していないか）
        var skipsDb = extra.Contains("--dry-run") || extra.Contains("--skip-db");
        var restores = extra.Contains("--restore-json") && !skipsDb;
        if (!skipsDb) Assert.Contains(s1, l => l.Contains("Name=z"));
        if (restores) Assert.Contains(s1, l => l.StartsWith("assets/b/asset.json"));
        else Assert.DoesNotContain(s1, l => l.StartsWith("assets/b/asset.json"));

        // JSON に結果が入っている
        using var doc = JsonDocument.Parse(so2);
        Assert.Equal(extra.Contains("--dry-run"), doc.RootElement.GetProperty("dryRun").GetBoolean());
        if (!skipsDb) Assert.True(doc.RootElement.GetProperty("database").GetProperty("assets").GetInt32() >= 1, so2);
        if (restores) Assert.True(doc.RootElement.GetProperty("assetJsonRestored").GetInt32() >= 1, so2);
    }
}
