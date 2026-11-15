using System.Text.RegularExpressions;

namespace DesignAid.Tests.Application;

/// <summary>
/// 装置の状態を書き換える入口が、プロジェクト共通のロック（ProjectLock）に参加していることを、
/// ソースの走査で確かめる静的な検査。入口を足したときの参加の漏れを自動で捕まえる。
/// <para>
/// コメントと文字列リテラルを除いてから照らし合わせ、<c>using</c> で取得する形（ハンドルを解放する形）を要求する。
/// 1 つのファイルに入口が複数あるときは、メソッドごとに見る。
/// </para>
/// </summary>
public class ProjectLockCoverageTests
{
    /// <summary>書き込みを示す呼び出し。</summary>
    private static readonly Regex WriteMarkers = new(
        @"SaveChanges|File\.(Write|Copy|Delete|Move|Replace|Create|AppendAll)|new\s+StreamWriter|new\s+FileStream|" +
        @"Directory\.(Delete|Move|CreateDirectory)|ZipFile\.(ExtractToDirectory|CreateFromDirectory)|ForceDeleteDirectory|" +
        @"ExecuteDelete|ExecuteSql|Process\.Start|\.AddAsync\(|\.LinkToAssetAsync\(|\.SyncAllAsync\(",
        RegexOptions.Compiled);

    /// <summary>ロックの取得。<c>using</c> の宣言か文の中で、取得の呼び出しをしていること。</summary>
    private static readonly Regex LockUse = new(
        @"using\s+(?:var\s+\w+|\(\s*var\s+\w+)\s*=[^;]*?(?:AcquireProjectLock\w*|ProjectLock\s*\.\s*TryAcquire)",
        RegexOptions.Compiled);

    private static readonly Regex HandlerName = new(
        @"CommandHandler\.Create(?:<[^;(]*>)?\(\s*(\w+)\s*\)", RegexOptions.Compiled);

    private static readonly Regex MemberStart = new(
        @"(?m)^    (?=[^\s{}])", RegexOptions.Compiled);

    /// <summary>コメントと文字列リテラルを取り除く（最初に現れたものを優先する 1 回の走査）。</summary>
    public static string StripCommentsAndStrings(string src)
        => Regex.Replace(src,
            "//[^\\r\\n]*|/\\*[\\s\\S]*?\\*/|\"\"\"[\\s\\S]*?\"\"\"|@\"(?:[^\"]|\"\")*\"|\\$?\"(?:[^\"\\\\\\r\\n]|\\\\.)*\"|'(?:[^'\\\\]|\\\\.)'",
            m => m.Value.StartsWith("//") || m.Value.StartsWith("/*") ? " " : "\"\"");

    /// <summary>
    /// 参加させない入口（キー → 理由）。キーは <c>ファイル</c> か <c>ファイル#メソッド</c>。
    /// 理由のないものは足さない。
    /// </summary>
    private static readonly Dictionary<string, string> Exempt = new()
    {
        ["Commands/CommandHelper.cs"] = "ロック自体と、ForceDeleteDirectory などの部品の実装（呼び出し側の入口がロックを取る）",
        ["Commands/ConfigCommand.cs"] = "config set は設定値（Settings の表）だけを書く。装置の状態（フォルダ・asset.json・リンク・索引）ではない",
        ["Commands/Dashboard/DashboardCommand.cs"] = "書くのは PID ファイルだけ（装置の状態ではない）",
        ["Commands/Dashboard/DashboardStopCommand.cs"] = "PID ファイルを消すだけ（装置の状態ではない）",
        ["Commands/UpdateCommand.cs"] = "ツール自体の更新で、プロジェクトのデータに触れない",
        ["Commands/Asset/AssetBomCommand.cs"] = "--export の出力先は利用者が指定する任意のファイルで、装置の状態ではない",
        ["Commands/Asset/AssetRenameCommand.cs"] = "AssetRenameService.RenameAsync がロックを取る（コマンドは結果を表示するだけ）",
        ["Commands/CheckCommand.cs"] = "整合性の検査。書き込みはレポートの出力だけ",
        ["Commands/VerifyCommand.cs"] = "検査のレポートの出力だけ",
        ["Commands/SearchCommand.cs"] = "検索のキャッシュ（hnsw_index.bin）の書き込みだけ。装置の状態ではなくキャッシュで、" +
            "一時ファイルへ書いてから置き換える（原子的）。読む側は判定値がずれていれば使わず作り直す",
        ["Mcp/DesignAidMcpTools.cs#SearchDesigns"] = "検索のキャッシュの書き込みだけ（SearchCommand と同じ。キャッシュであって装置の状態ではなく、原子的に書く）",
    };

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "DesignAid.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("リポジトリのルートが見つかりません");
    }

    private static string MethodName(string chunk) => Regex.Match(chunk, @"(\w+)\s*\(").Groups[1].Value;

    /// <summary>参加していない入口の一覧（ファイル#メソッド）。空であるべき。</summary>
    public static List<string> FindUnlockedEntries(string srcRoot, IReadOnlyDictionary<string, string> exempt)
    {
        var problems = new List<string>();

        // コマンド: ハンドラとして登録されたメソッドを入口とし、メソッドごとに見る
        foreach (var file in Directory.GetFiles(Path.Combine(srcRoot, "Commands"), "*.cs", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(srcRoot, file).Replace('\\', '/');
            if (exempt.ContainsKey(rel)) continue;
            var text = StripCommentsAndStrings(File.ReadAllText(file));
            if (!WriteMarkers.IsMatch(text)) continue;

            var chunks = MemberStart.Split(text);
            var handlers = HandlerName.Matches(text).Select(m => m.Groups[1].Value).Distinct().ToList();
            if (handlers.Count == 0) continue; // ハンドラを持たない部品（入口ではない）
            foreach (var h in handlers)
            {
                if (exempt.ContainsKey($"{rel}#{h}")) continue;
                var entry = chunks.FirstOrDefault(c => MethodName(c) == h);
                if (entry == null || !LockUse.IsMatch(entry)) problems.Add($"{rel}#{h}");
            }
        }

        // ダッシュボードのサービスと MCP のツール: public メソッドごと
        foreach (var rel in new[] { "Dashboard/Services/DashboardService.cs", "Mcp/DesignAidMcpTools.cs" })
        {
            var text = StripCommentsAndStrings(File.ReadAllText(Path.Combine(srcRoot, rel)));
            foreach (var chunk in MemberStart.Split(text))
            {
                if (!chunk.TrimStart().StartsWith("public") || !WriteMarkers.IsMatch(chunk)) continue;
                var name = MethodName(chunk);
                if (exempt.ContainsKey($"{rel}#{name}")) continue;
                if (!LockUse.IsMatch(chunk)) problems.Add($"{rel}#{name}");
            }
        }
        return problems;
    }

    [Fact]
    public void 装置の状態を書き換える入口は_すべてProjectLockにusingで参加している()
    {
        var problems = FindUnlockedEntries(Path.Combine(RepoRoot(), "src", "DesignAid"), Exempt);
        Assert.True(problems.Count == 0, "ProjectLock に参加していない入口: " + string.Join(", ", problems));
    }

    [Fact]
    public void 参加させない入口の理由は_空でない()
    {
        Assert.All(Exempt, kv => Assert.False(string.IsNullOrWhiteSpace(kv.Value), kv.Key));
    }

    private static void Write(string root, string rel, string text)
    {
        var path = Path.Combine(root, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    [Fact]
    public void 検査自体が働く_ロックを使わない入口や緩い参加の形は検出される()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "design-aid-cover-" + Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(tmp, "Commands"));
        try
        {
            // ロックを取らない
            Write(tmp, "Commands/BadCommand.cs", "class X {\n    X() { Handler = CommandHandler.Create(E); }\n    void E() { File.WriteAllText(a, b); }\n}");
            // 正しい（using で取得）
            Write(tmp, "Commands/GoodCommand.cs", "class Y {\n    Y() { Handler = CommandHandler.Create(E); }\n    void E() { using var l = CommandHelper.AcquireProjectLock(); File.WriteAllText(a, b); }\n}");
            // コメントだけ・文字列だけに名前がある
            Write(tmp, "Commands/CommentCommand.cs", "class C {\n    C() { Handler = CommandHandler.Create(E); }\n    void E() {\n        // using var l = ProjectLock.TryAcquire(x);\n        File.Move(a, b);\n    }\n}");
            Write(tmp, "Commands/StringCommand.cs", "class S {\n    S() { Handler = CommandHandler.Create(E); }\n    void E() { var s = \"using var l = ProjectLock.TryAcquire(x);\"; File.Create(a); }\n}");
            // 取得はしているが using ではない（解放されない形）
            Write(tmp, "Commands/NoUsingCommand.cs", "class N {\n    N() { Handler = CommandHandler.Create(E); }\n    void E() { var l = ProjectLock.TryAcquire(x); new FileStream(a, m); }\n}");
            // 1 つのファイルに入口が 2 つ: 片方だけ参加
            Write(tmp, "Commands/TwoCommand.cs",
                "class T {\n    T() { A = CommandHandler.Create(First); B = CommandHandler.Create<int, Task<int>>(Second); }\n" +
                "    void First() { using var l = CommandHelper.AcquireProjectLock(); File.Delete(a); }\n" +
                "    Task<int> Second(int x) { Process.Start(p); return null; }\n}");
            // 書き込みの目印: ExecuteSql / ExecuteDelete
            Write(tmp, "Commands/SqlCommand.cs", "class Q {\n    Q() { Handler = CommandHandler.Create(E); }\n    void E() { db.Database.ExecuteSqlRaw(x); }\n}");
            Directory.CreateDirectory(Path.Combine(tmp, "Dashboard", "Services"));
            Directory.CreateDirectory(Path.Combine(tmp, "Mcp"));
            Write(tmp, "Dashboard/Services/DashboardService.cs",
                "class D {\n    public void Bad() { ctx.SaveChanges(); }\n    public void Good() { using var l = ProjectLock.TryAcquire(x); ctx.SaveChanges(); }\n    public void Read() { }\n}");
            Write(tmp, "Mcp/DesignAidMcpTools.cs", "class M {\n    public void Add() { svc.AddAsync(a); }\n}");

            var problems = FindUnlockedEntries(tmp, new Dictionary<string, string>());

            Assert.Contains("Commands/BadCommand.cs#E", problems);
            Assert.DoesNotContain("Commands/GoodCommand.cs#E", problems);
            Assert.Contains("Commands/CommentCommand.cs#E", problems);
            Assert.Contains("Commands/StringCommand.cs#E", problems);
            Assert.Contains("Commands/NoUsingCommand.cs#E", problems);
            Assert.Contains("Commands/TwoCommand.cs#Second", problems);
            Assert.DoesNotContain("Commands/TwoCommand.cs#First", problems);
            Assert.Contains("Commands/SqlCommand.cs#E", problems);
            Assert.Contains("Dashboard/Services/DashboardService.cs#Bad", problems);
            Assert.DoesNotContain(problems, p => p.EndsWith("#Good") || p.EndsWith("#Read"));
            Assert.Contains("Mcp/DesignAidMcpTools.cs#Add", problems);
        }
        finally
        {
            Directory.Delete(tmp, true);
        }
    }

    [Fact]
    public void コメントと文字列の除去は_コード部分を残す()
    {
        var stripped = StripCommentsAndStrings("a(); // c \"q\"\nb = \"x // y\"; /* z */ c();\nvar r = @\"p\"\"q\";");
        Assert.Contains("a();", stripped);
        Assert.Contains("c();", stripped);
        Assert.DoesNotContain("x // y", stripped);
        Assert.DoesNotContain("z", stripped);
    }
}
