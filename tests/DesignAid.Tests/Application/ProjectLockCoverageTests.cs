using System.Text.RegularExpressions;

namespace DesignAid.Tests.Application;

/// <summary>
/// 装置の状態を書き換える入口が、プロジェクト共通のロック（ProjectLock）に参加していることを、
/// ソースの走査で確かめる静的な検査。入口を足したときの参加の漏れを自動で捕まえる。
/// </summary>
public class ProjectLockCoverageTests
{
    /// <summary>書き込みを示す呼び出し。</summary>
    private static readonly Regex WriteMarkers = new(
        @"SaveChanges|File\.Write|File\.Copy|File\.Delete|Directory\.Delete|Directory\.Move|Directory\.CreateDirectory|" +
        @"ZipFile\.(ExtractToDirectory|CreateFromDirectory)|ForceDeleteDirectory|\.AddAsync\(|\.LinkToAssetAsync\(|\.SyncAllAsync\(",
        RegexOptions.Compiled);

    private static readonly Regex LockUse = new(@"AcquireProjectLock|ProjectLock", RegexOptions.Compiled);

    /// <summary>
    /// 参加させない入口（ファイル名 → 理由）。理由のないものは足さない。
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
    };

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "DesignAid.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("リポジトリのルートが見つかりません");
    }

    /// <summary>参加していない入口の一覧（ファイルまたはメソッド）。空であるべき。</summary>
    public static List<string> FindUnlockedEntries(string srcRoot, IReadOnlyDictionary<string, string> exempt)
    {
        var problems = new List<string>();

        // コマンド: 1 ファイル = 1 入口
        foreach (var file in Directory.GetFiles(Path.Combine(srcRoot, "Commands"), "*.cs", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(srcRoot, file).Replace('\\', '/');
            if (exempt.ContainsKey(rel)) continue;
            var text = File.ReadAllText(file);
            if (WriteMarkers.IsMatch(text) && !LockUse.IsMatch(text)) problems.Add(rel);
        }

        // ダッシュボードのサービスと MCP のツール: 1 メソッド = 1 入口
        foreach (var rel in new[] { "Dashboard/Services/DashboardService.cs", "Mcp/DesignAidMcpTools.cs" })
        {
            var text = File.ReadAllText(Path.Combine(srcRoot, rel));
            var chunks = Regex.Split(text, @"(?m)^    (?=(?:public|private|internal|protected)\b)");
            foreach (var chunk in chunks)
            {
                if (!WriteMarkers.IsMatch(chunk) || LockUse.IsMatch(chunk)) continue;
                var name = Regex.Match(chunk, @"(\w+)\s*\(").Groups[1].Value;
                problems.Add($"{rel}#{name}");
            }
        }
        return problems;
    }

    [Fact]
    public void 装置の状態を書き換える入口は_すべてProjectLockに参加している()
    {
        var problems = FindUnlockedEntries(Path.Combine(RepoRoot(), "src", "DesignAid"), Exempt);
        Assert.True(problems.Count == 0, "ProjectLock に参加していない入口: " + string.Join(", ", problems));
    }

    [Fact]
    public void 参加させない入口の理由は_空でない()
    {
        Assert.All(Exempt, kv => Assert.False(string.IsNullOrWhiteSpace(kv.Value), kv.Key));
    }

    [Fact]
    public void 検査自体が働く_ロックを使わない書き込みの入口は検出される()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "design-aid-cover-" + Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(tmp, "Commands"));
        Directory.CreateDirectory(Path.Combine(tmp, "Dashboard", "Services"));
        Directory.CreateDirectory(Path.Combine(tmp, "Mcp"));
        try
        {
            File.WriteAllText(Path.Combine(tmp, "Commands", "BadCommand.cs"), "class X { void E() { File.WriteAllText(\"a\", \"b\"); } }");
            File.WriteAllText(Path.Combine(tmp, "Commands", "GoodCommand.cs"), "class Y { void E() { using var l = CommandHelper.AcquireProjectLock(); File.WriteAllText(\"a\", \"b\"); } }");
            File.WriteAllText(Path.Combine(tmp, "Dashboard", "Services", "DashboardService.cs"),
                "class D {\n    public void Bad() { ctx.SaveChanges(); }\n    public void Good() { ProjectLock.TryAcquire(x); ctx.SaveChanges(); }\n    public void Read() { }\n}");
            File.WriteAllText(Path.Combine(tmp, "Mcp", "DesignAidMcpTools.cs"),
                "class M {\n    public void Add() { svc.AddAsync(a); }\n}");

            var problems = FindUnlockedEntries(tmp, new Dictionary<string, string>());

            Assert.Contains("Commands/BadCommand.cs", problems);
            Assert.DoesNotContain("Commands/GoodCommand.cs", problems);
            Assert.Contains(problems, p => p.EndsWith("#Bad"));
            Assert.DoesNotContain(problems, p => p.EndsWith("#Good") || p.EndsWith("#Read"));
            Assert.Contains(problems, p => p.StartsWith("Mcp/") && p.EndsWith("#Add"));
        }
        finally
        {
            Directory.Delete(tmp, true);
        }
    }
}
