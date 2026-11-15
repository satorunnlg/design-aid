using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
using System.Text.Json;
using DesignAid.Commands;

namespace DesignAid.Tests.Application;

/// <summary>
/// 全コマンドの失敗の契約の検査（#8・#10）。
/// <list type="bullet">
/// <item>失敗の経路で、終了コードが 0 以外になる（<c>Environment.ExitCode</c> を設定するだけのハンドラも、Main の戻り値に届く）。</item>
/// <item><c>--json</c> を持つコマンドは、失敗しても標準出力が有効な JSON 1 つになる（診断は標準エラー）。</item>
/// </list>
/// 全コマンドは <see cref="Program.BuildRootCommand"/> から列挙するので、コマンドを足すと自動で検査の対象になる。
/// プロジェクトの無い空のディレクトリと、壊れたプロジェクト（DB・asset.json・archive_index.json が壊れている）で動かす。
/// 実データには触れない（すべて一時ディレクトリ）。
/// </summary>
[Collection("CommandEnv")]
public class CommandFailureContractTests : IDisposable
{
    private readonly string _base = Path.Combine(Path.GetTempPath(), "design-aid-contract-tests", Guid.NewGuid().ToString());
    private readonly string? _savedEnv = Environment.GetEnvironmentVariable("DA_DATA_DIR");
    private readonly string _savedCwd = Directory.GetCurrentDirectory();

    public CommandFailureContractTests() => Directory.CreateDirectory(_base);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("DA_DATA_DIR", _savedEnv);
        Directory.SetCurrentDirectory(_savedCwd);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { CommandHelperForTests.ForceDelete(_base); } catch { }
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// プロジェクトが無くても動く（または副作用が大きくて検査では動かせない）最上位のコマンドと、その理由。
    /// 他のコマンドは、プロジェクトが無ければ終了コード 3 で失敗する。
    /// </summary>
    private static readonly Dictionary<string, string> NoProjectNeeded = new()
    {
        ["setup"] = "プロジェクトを初期化するコマンドで、プロジェクトが無いのが前提（実行すると DB を作ってしまう）",
        ["update"] = "ツール自体の更新。ネットワークに出る・自分自身を差し替える",
        ["mcp"] = "MCP サーバーを起動する（標準入出力で待ち続ける）",
        ["dashboard"] = "Web サーバーを起動する（待ち続ける）",
        ["restore"] = "復元元の ZIP を引数で受け、プロジェクトの外へ展開できる。プロジェクトが無いこと以外の理由で止まる",
    };

    private static IEnumerable<(string[] Path, Command Cmd)> Leaves(Command command, string[] prefix)
    {
        var subs = command.Subcommands.ToList();
        if (subs.Count == 0)
        {
            yield return (prefix, command);
            yield break;
        }
        foreach (var sub in subs)
            foreach (var leaf in Leaves(sub, prefix.Append(sub.Name).ToArray()))
                yield return leaf;
    }

    private static List<(string[] Path, Command Cmd)> AllLeaves()
        => Leaves(Program.BuildRootCommand(), Array.Empty<string>()).ToList();

    private static string Dummy(Type t)
        => t == typeof(int) || t == typeof(long) || t == typeof(decimal) || t == typeof(double) ? "1" : "x";

    /// <summary>必須の引数・オプションを、型に合うダミー値で埋めたコマンドライン。</summary>
    private static string[] ArgsFor((string[] Path, Command Cmd) leaf, bool json)
    {
        // 引数の検査がプロジェクトの検索より先にあるコマンドは、検査を通る値にする（プロジェクトが無い失敗に届かせるため）
        var key = string.Join(' ', leaf.Path);
        if (key == "archive restore") return json ? new[] { "archive", "restore", "asset", "x", "--json" } : new[] { "archive", "restore", "asset", "x" };
        var args = new List<string>(leaf.Path);
        if (key == "asset update") args.AddRange(new[] { "--description", "x" });
        foreach (var a in leaf.Cmd.Arguments)
            if (a.Arity.MinimumNumberOfValues >= 1) args.Add(Dummy(a.ValueType));
        foreach (var o in leaf.Cmd.Options)
            if (o.IsRequired) { args.Add(o.Aliases.First()); args.Add(Dummy(o.ValueType)); }
        if (json) args.Add("--json");
        return args.ToArray();
    }

    private static bool HasJson(Command c) => c.Options.Any(o => o.HasAlias("--json"));

    /// <summary>Main と同じ経路（Program.RunAsync）で実行し、(終了コード, 標準出力, 標準エラー) を返す。</summary>
    private static async Task<(int Code, string Out, string Err)> RunAsync(params string[] args)
    {
        var so = Console.Out; var se = Console.Error;
        var outW = new StringWriter(); var errW = new StringWriter();
        Console.SetOut(outW); Console.SetError(errW);
        Environment.ExitCode = 0;
        try
        {
            var code = await Program.RunAsync(args);
            return (code, outW.ToString(), errW.ToString());
        }
        finally
        {
            Console.SetOut(so); Console.SetError(se);
            Environment.ExitCode = 0;
        }
    }

    private void UseEmptyDir()
    {
        var d = Path.Combine(_base, "empty-" + Guid.NewGuid().ToString("N")[..8], "a", "b", "c");
        Directory.CreateDirectory(d);
        Environment.SetEnvironmentVariable("DA_DATA_DIR", "");
        Directory.SetCurrentDirectory(d);
    }

    /// <summary>DB・asset.json・part.json・archive_index.json が壊れたプロジェクト。</summary>
    private void UseBrokenProject()
    {
        var d = Path.Combine(_base, "broken-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(d, "assets", "a1"));
        Directory.CreateDirectory(Path.Combine(d, "components", "p1"));
        File.WriteAllText(Path.Combine(d, "design_aid.db"), "これは SQLite ではない");
        File.WriteAllText(Path.Combine(d, "assets", "a1", "asset.json"), "{ broken");
        File.WriteAllText(Path.Combine(d, "components", "p1", "part.json"), "{ broken");
        File.WriteAllText(Path.Combine(d, "archive_index.json"), "{ broken");
        Environment.SetEnvironmentVariable("DA_DATA_DIR", d);
        Directory.SetCurrentDirectory(d);
    }

    private static string Describe(string[] args, int code, string stdout, string stderr)
        => $"[{string.Join(' ', args)}] code={code} stdout=<{stdout.Trim()}> stderr=<{stderr.Trim()}>";

    [Fact]
    public void 走査が全コマンドを見ている_葉は30個以上_json付きは12個以上()
    {
        var leaves = AllLeaves();
        Assert.True(leaves.Count >= 30, $"葉のコマンドが {leaves.Count} 個しか無い（列挙が壊れた）");
        Assert.True(leaves.Count(l => HasJson(l.Cmd)) >= 12, "--json を持つコマンドが 12 個未満（列挙が壊れた）");
    }

    [Fact]
    public void json付きのコマンドは_すべてJsonGuardで包まれている_呼び出し側の固定()
    {
        var unguarded = AllLeaves()
            .Where(l => HasJson(l.Cmd) && l.Cmd.Handler?.GetType().DeclaringType != typeof(JsonGuard))
            .Select(l => string.Join(' ', l.Path)).ToList();
        Assert.True(unguarded.Count == 0, "JsonGuard.Wrap を通していない --json のコマンド: " + string.Join(", ", unguarded));
    }

    [Fact]
    public async Task プロジェクトが無い_全コマンドが終了コード3で失敗する_診断はstderr()
    {
        UseEmptyDir();
        var bad = new List<string>();
        var checkedCount = 0;
        foreach (var leaf in AllLeaves())
        {
            if (NoProjectNeeded.ContainsKey(leaf.Path[0])) continue;
            var args = ArgsFor(leaf, json: false);
            var (code, so, se) = await RunAsync(args);
            checkedCount++;
            if (code != 3 || !se.Contains("プロジェクトが見つかりません")) bad.Add(Describe(args, code, so, se));
        }
        Assert.True(checkedCount >= 25, $"検査したコマンドが {checkedCount} 個しか無い");
        Assert.True(bad.Count == 0, "失敗の経路で終了コード 3 にならないコマンド:\n" + string.Join("\n", bad));
    }

    [Fact]
    public async Task プロジェクトが無い_jsonのコマンドは_標準出力が有効なJSON1つ_success_false_終了コードは同じ()
    {
        UseEmptyDir();
        var bad = new List<string>();
        foreach (var leaf in AllLeaves().Where(l => HasJson(l.Cmd)))
        {
            var (plainCode, _, _) = await RunAsync(ArgsFor(leaf, json: false));
            var args = ArgsFor(leaf, json: true);
            var (code, so, se) = await RunAsync(args);
            var ok = JsonGuard.IsSingleJson(so) && code == plainCode && code != 0 && se.Contains("プロジェクトが見つかりません");
            if (ok)
            {
                using var doc = JsonDocument.Parse(so);
                ok = doc.RootElement.ValueKind == JsonValueKind.Object
                     && doc.RootElement.TryGetProperty("success", out var s) && s.ValueKind == JsonValueKind.False;
            }
            if (!ok) bad.Add(Describe(args, code, so, se));
        }
        Assert.True(bad.Count == 0, "--json の失敗で、標準出力が失敗の JSON 1 つにならないコマンド:\n" + string.Join("\n", bad));
    }

    [Fact]
    public async Task 壊れたプロジェクト_jsonのコマンドは_何が起きても標準出力が有効なJSON1つ()
    {
        UseBrokenProject();
        var bad = new List<string>();
        foreach (var leaf in AllLeaves().Where(l => HasJson(l.Cmd)))
        {
            var args = ArgsFor(leaf, json: true);
            var (code, so, se) = await RunAsync(args);
            if (!JsonGuard.IsSingleJson(so)) { bad.Add(Describe(args, code, so, se)); continue; }
            // 失敗（0 以外）なら、成功を名乗らない
            if (code != 0)
            {
                using var doc = JsonDocument.Parse(so);
                if (doc.RootElement.ValueKind == JsonValueKind.Object
                    && doc.RootElement.TryGetProperty("success", out var s) && s.ValueKind == JsonValueKind.True)
                    bad.Add("失敗なのに success=true: " + Describe(args, code, so, se));
            }
        }
        Assert.True(bad.Count == 0, "壊れたプロジェクトで標準出力が有効な JSON 1 つにならないコマンド:\n" + string.Join("\n", bad));
    }

    [Fact]
    public async Task 例外で落ちるコマンド_jsonなら失敗のJSON1つ_診断はstderr_終了コード1()
    {
        // 例外を投げるハンドラを JsonGuard で包んで、InvokeAsync の経路で動かす
        // （壊れたプロジェクトで例外が出るかどうかに依存しない検査）
        var cmd = new Command("boom") { new Option<bool>("--json") };
        cmd.Handler = JsonGuard.Wrap(cmd, CommandHandler.Create<bool>((Action<bool>)((bool json) =>
        {
            Console.WriteLine("{\"success\":true}");
            throw new InvalidOperationException("注入した例外");
        })));
        var root = new RootCommand { cmd };

        var so = Console.Out; var se = Console.Error;
        var outW = new StringWriter(); var errW = new StringWriter();
        Console.SetOut(outW); Console.SetError(errW);
        int code;
        try { code = await root.InvokeAsync(new[] { "boom", "--json" }); }
        finally { Console.SetOut(so); Console.SetError(se); Environment.ExitCode = 0; }

        Assert.Equal(1, code);
        Assert.True(JsonGuard.IsSingleJson(outW.ToString()), outW.ToString());
        using var doc = JsonDocument.Parse(outW.ToString());
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean()); // 成功を出してから落ちても、成功を名乗らない
        Assert.Contains("注入した例外", doc.RootElement.GetProperty("errors")[0].GetString());
        Assert.Contains("注入した例外", errW.ToString());
    }

    [Fact]
    public async Task ExitCodeを設定するだけのハンドラの失敗が_Mainの戻り値に届く()
    {
        // #8 の再現そのもの: 戻り値を返さずに Environment.ExitCode だけを設定するハンドラ
        var cmd = new Command("fail");
        cmd.Handler = CommandHandler.Create(() => { Environment.ExitCode = 4; });
        var root = new RootCommand { cmd };
        Environment.ExitCode = 0;
        var invoked = await root.InvokeAsync(new[] { "fail" });
        try
        {
            Assert.Equal(0, invoked); // System.CommandLine は拾わない（これが #8 の原因）
            Assert.Equal(4, Program.ResolveExitCode(invoked, Environment.ExitCode)); // Main の補正で届く
        }
        finally { Environment.ExitCode = 0; }
    }

    [Fact]
    public void ResolveExitCode_戻り値が0のときだけEnvironmentのExitCodeを採る()
    {
        Assert.Equal(0, Program.ResolveExitCode(0, 0));
        Assert.Equal(3, Program.ResolveExitCode(0, 3));
        Assert.Equal(1, Program.ResolveExitCode(1, 0));
        Assert.Equal(2, Program.ResolveExitCode(2, 5));
    }

    [Fact]
    public void ResolveStdout_成功は_そのまま()
    {
        Assert.Equal("", JsonGuard.ResolveStdout("", Array.Empty<string>(), 0, null));
        Assert.Equal("{\"a\":1}", JsonGuard.ResolveStdout("{\"a\":1}", Array.Empty<string>(), 0, null));
    }

    [Fact]
    public void ResolveStdout_失敗_出力が空_途中まで_複数のJSONは_失敗のJSON1つに置き換える_有効な1つはそのまま()
    {
        var lines = new[] { "[ERROR] 壊れた", "  対処: 直す" };
        foreach (var bad in new[] { "", "   ", "{ \"a\": ", "{} {}", "text" })
        {
            var r = JsonGuard.ResolveStdout(bad, lines, 1, null);
            Assert.True(JsonGuard.IsSingleJson(r), $"[{bad}] -> [{r}]");
            using var doc = JsonDocument.Parse(r);
            Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
            Assert.Equal("壊れた", doc.RootElement.GetProperty("errors")[0].GetString());
            Assert.Equal(1, doc.RootElement.GetProperty("exitCode").GetInt32());
        }
        const string own = "{\"success\":false,\"x\":1}";
        Assert.Equal(own, JsonGuard.ResolveStdout(own, lines, 5, null)); // コマンド自身の失敗の JSON は尊重する
        // stderr が空でも errors は空にならない
        using var d2 = JsonDocument.Parse(JsonGuard.ResolveStdout("", Array.Empty<string>(), 7, null));
        Assert.Equal(1, d2.RootElement.GetProperty("errors").GetArrayLength());
    }
}
