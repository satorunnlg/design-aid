using System.CommandLine;
using System.Text.Json;
using DesignAid.Commands;

namespace DesignAid.Tests.Application;

/// <summary>
/// <c>--json</c> の標準出力の契約の検査（#10）: 成功の経路でも、引数の解析に失敗する経路でも、標準出力は JSON 1 つだけ。
/// 人向けの文（進捗・[SUCCESS]・ヘルプ）は標準エラーへ出る。
/// コマンドは <see cref="Program.BuildRootCommand"/> から列挙するので、<c>--json</c> を持つコマンドを足すと、
/// この検査の対象に自動で入り、成功の引数の表（<see cref="SuccessArgs"/>）に無ければ落ちる。
/// 実データには触れない（すべて一時ディレクトリ）。
/// </summary>
[Collection("CommandEnv")]
public partial class JsonStdoutContractTests : IDisposable
{
    private readonly string _base = Path.Combine(Path.GetTempPath(), "design-aid-json-stdout-tests", Guid.NewGuid().ToString());
    private readonly string? _savedEnv = Environment.GetEnvironmentVariable("DA_DATA_DIR");
    private readonly string _savedCwd = Directory.GetCurrentDirectory();

    public JsonStdoutContractTests() => Directory.CreateDirectory(_base);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("DA_DATA_DIR", _savedEnv);
        Directory.SetCurrentDirectory(_savedCwd);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { CommandHelperForTests.ForceDelete(_base); } catch { }
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// <c>--json</c> を持つ各コマンドを、正しく作ったプロジェクトで成功させる引数（<c>--json</c> は検査が足す）。
    /// プロジェクトには装置 a・b（a が b を子に持つ）とパーツ P-1 がある。
    /// 状態を変えるコマンドは最後（<c>asset rename</c>）にしか置かない。
    /// </summary>
    private static readonly (string Key, string[] Args)[] SuccessArgs =
    [
        ("archive list", ["archive", "list"]),
        ("asset bom", ["asset", "bom", "a"]),
        ("asset list", ["asset", "list"]),
        ("check", ["check"]),
        ("config show", ["config", "show"]),
        ("deploy", ["deploy", "--dry-run", "--no-confirm"]),
        ("part list", ["part", "list"]),
        ("search", ["search", "P-1", "--local"]),
        ("status", ["status"]),
        ("sync", ["sync"]),
        ("verify", ["verify"]),
        ("asset rename", ["asset", "rename", "b", "c"]),
    ];

    private static IEnumerable<(string Key, Command Cmd)> Leaves(Command command, string[] prefix)
    {
        var subs = command.Subcommands.ToList();
        if (subs.Count == 0)
        {
            yield return (string.Join(' ', prefix), command);
            yield break;
        }
        foreach (var sub in subs)
            foreach (var leaf in Leaves(sub, prefix.Append(sub.Name).ToArray()))
                yield return leaf;
    }

    private static List<string> JsonLeafKeys()
        => Leaves(Program.BuildRootCommand(), Array.Empty<string>())
            .Where(l => l.Cmd.Options.Any(o => o.HasAlias("--json")))
            .Select(l => l.Key).ToList();

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

    /// <summary>setup と asset add・part add・asset link で、正しく作ったプロジェクト（装置 a・b、パーツ P-1）を作る。</summary>
    private async Task<string> CreateHealthyProjectAsync()
    {
        var root = Path.Combine(_base, "proj-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        Environment.SetEnvironmentVariable("DA_DATA_DIR", "");
        Directory.SetCurrentDirectory(root);
        foreach (var args in new[]
        {
            new[] { "setup" },
            new[] { "asset", "add", "a", "--no-git" },
            new[] { "asset", "add", "b", "--no-git" },
            new[] { "part", "add", "P-1", "--name", "部品", "--no-git" },
            new[] { "asset", "link", "a", "--child", "b" },
        })
        {
            var (code, so, se) = await RunAsync(args);
            Assert.True(code == 0, $"準備に失敗 [{string.Join(' ', args)}] code={code} out=<{so}> err=<{se}>");
        }
        return root;
    }

    private static string Describe(string[] args, int code, string so, string se)
        => $"[{string.Join(' ', args)}] code={code} stdout=<{so.Trim()}> stderr=<{se.Trim()}>";

    [Fact]
    public void 成功の引数の表は_jsonを持つ全コマンドを覆っている_漏れなく列挙()
    {
        var keys = JsonLeafKeys();
        Assert.True(keys.Count >= 12, $"--json を持つコマンドが {keys.Count} 個しか無い（列挙が壊れた）");
        var missing = keys.Where(k => SuccessArgs.All(s => s.Key != k)).ToList();
        Assert.True(missing.Count == 0, "成功の引数の表に無い --json のコマンド（表に足すこと）: " + string.Join(", ", missing));
        var stale = SuccessArgs.Where(s => !keys.Contains(s.Key)).Select(s => s.Key).ToList();
        Assert.True(stale.Count == 0, "表にあるが --json を持たないコマンド: " + string.Join(", ", stale));
    }

    [Fact]
    public async Task 正しいプロジェクト_jsonのコマンドは_成功の経路で標準出力がJSON1つ()
    {
        await CreateHealthyProjectAsync();
        var bad = new List<string>();
        foreach (var (key, baseArgs) in SuccessArgs)
        {
            var args = baseArgs.Append("--json").ToArray();
            var (code, so, se) = await RunAsync(args);
            if (code != 0 || !JsonGuard.IsSingleJson(so)) bad.Add(Describe(args, code, so, se));
        }
        Assert.True(bad.Count == 0, "成功の経路で標準出力が JSON 1 つにならないコマンド:\n" + string.Join("\n", bad));
    }

    /// <summary>オプションの組み合わせ違い（別の分岐を通る）。<c>--json</c> は検査が足す。</summary>
    private static readonly string[][] VariantArgs =
    [
        ["asset", "bom", "a", "--include-subassets"],
        ["asset", "bom", "a", "--export", "csv"],
        ["asset", "list", "--verbose"],
        ["asset", "list", "--status", "active"],
        ["check", "--duplicates"],
        ["check", "--verbose"],
        ["search", "P-1"],
        ["status", "--asset", "a"],
        ["sync", "--dry-run"],
        ["sync", "--force", "--include-vectors", "--restore-json"],
        ["sync", "--skip-db"],
        ["verify", "--part", "P-1"],
        ["asset", "rename", "a", "a2", "--quiet"],
    ];

    [Fact]
    public async Task 正しいプロジェクト_オプションの組み合わせ違いでも_成功の経路で標準出力がJSON1つ()
    {
        await CreateHealthyProjectAsync();
        var bad = new List<string>();
        foreach (var baseArgs in VariantArgs)
        {
            var args = baseArgs.Append("--json").ToArray();
            var (code, so, se) = await RunAsync(args);
            if (code != 0 || !JsonGuard.IsSingleJson(so)) bad.Add(Describe(args, code, so, se));
        }
        // deploy は実際に書く経路も（出力先は一時ディレクトリ）
        var deployArgs = new[] { "deploy", "--no-confirm", "--output", Path.Combine(_base, "deploy-out"), "--json" };
        var (dc, dso, dse) = await RunAsync(deployArgs);
        if (dc != 0 || !JsonGuard.IsSingleJson(dso)) bad.Add(Describe(deployArgs, dc, dso, dse));
        Assert.True(bad.Count == 0, "成功の経路で標準出力が JSON 1 つにならない組み合わせ:" + Environment.NewLine + string.Join(Environment.NewLine, bad));
    }

    [Fact]
    public async Task sync_json_componentsが無いプロジェクトでも_標準出力がJSON1つ_DB同期の文はstderr()
    {
        var root = await CreateHealthyProjectAsync();
        Directory.Delete(Path.Combine(root, "components"), recursive: true);
        var (code, so, se) = await RunAsync("sync", "--json");
        Assert.True(code == 0 && JsonGuard.IsSingleJson(so), Describe(new[] { "sync", "--json" }, code, so, se));
        Assert.Contains("Syncing to database", se); // 人向けの文は消えずに stderr へ
    }

    [Fact]
    public void HumanOutput_jsonなら標準出力を標準エラーへ向け_抜けたら戻す_jsonでなければ何もしない()
    {
        var so = Console.Out; var se = Console.Error;
        var outW = new StringWriter(); var errW = new StringWriter();
        Console.SetOut(outW); Console.SetError(errW);
        try
        {
            using (CommandHelper.HumanOutput(true)) Console.WriteLine("人向け");
            Console.WriteLine("JSON");
            using (CommandHelper.HumanOutput(false)) Console.WriteLine("そのまま");
            Assert.Equal("JSON" + Environment.NewLine + "そのまま" + Environment.NewLine, outW.ToString());
            Assert.Equal("人向け" + Environment.NewLine, errW.ToString());
        }
        finally { Console.SetOut(so); Console.SetError(se); }
    }

    [Theory]
    [InlineData("asset", "list", "--json", "--bogus")]
    [InlineData("bogus", "--json")]
    [InlineData("asset", "rename", "--json")]
    [InlineData("search", "--json")]
    [InlineData("--json", "--nonexistent-root-option")]
    public async Task jsonを付けたまま引数の解析に失敗_標準出力は失敗のJSON1つ_診断はstderr_終了コードは0以外(params string[] args)
    {
        var (code, so, se) = await RunAsync(args);
        Assert.NotEqual(0, code);
        Assert.True(JsonGuard.IsSingleJson(so), Describe(args, code, so, se));
        using var doc = JsonDocument.Parse(so);
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.True(doc.RootElement.GetProperty("errors").GetArrayLength() >= 1);
        Assert.Equal(code, doc.RootElement.GetProperty("exitCode").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(se), "診断が stderr に出ていない");
        Assert.DoesNotContain("Usage:", so);
        Assert.DoesNotContain("使用法", so);
    }

    [Fact]
    public async Task jsonなしの解析の失敗は_従来どおり_JSONを出さない()
    {
        var (code, so, se) = await RunAsync("asset", "list", "--bogus");
        Assert.NotEqual(0, code);
        Assert.False(JsonGuard.IsSingleJson(so), Describe(new[] { "asset", "list", "--bogus" }, code, so, se));
    }

    [Theory]
    [InlineData(new[] { "asset", "list", "--json" }, true)]
    [InlineData(new[] { "--json" }, true)]
    [InlineData(new[] { "asset", "list" }, false)]
    [InlineData(new[] { "search", "--", "--json" }, false)]
    [InlineData(new[] { "--jsonx" }, false)]
    public void HasJsonFlag_オプション終端より前の_json_だけを見る(string[] args, bool expected)
        => Assert.Equal(expected, Program.HasJsonFlag(args));
}
