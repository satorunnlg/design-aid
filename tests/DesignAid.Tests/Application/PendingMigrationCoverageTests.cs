using System.Text.RegularExpressions;

namespace DesignAid.Tests.Application;

/// <summary>
/// DbContext のファクトリ（<c>AddDesignAidServices</c>）を登録する入口が、登録の前に保留中のマイグレーションの経路
/// （<c>CommandHelper.CreateDbContext()</c> または <c>ApplyPendingMigrations</c>）を通ることを、
/// ソースの走査で呼び出し側から固定する静的な検査（#10）。ダッシュボード・MCP・DI の組み立てが対象。
/// 通さないと、旧版の DB のまま起動したとき、追加された列（Assets.PreviousNames など）で検索が失敗する。
/// <para>判定は純関数 <see cref="FindUnmigratedEntries"/> に切り出し、合成したソースで検出力を確かめる。</para>
/// </summary>
public class PendingMigrationCoverageTests
{
    private static readonly Regex MemberStart = new(@"(?m)^    (?=[^\s{}])", RegexOptions.Compiled);

    /// <summary>ファクトリを登録する呼び出し（宣言の <c>IServiceCollection AddDesignAidServices(</c> は含めない）。</summary>
    private static readonly Regex FactoryRegistration = new(@"(?<!IServiceCollection\s)\bAddDesignAidServices\s*\(", RegexOptions.Compiled);

    /// <summary>保留中のマイグレーションの経路。</summary>
    private static readonly Regex MigrationPath = new(@"\bCreateDbContext\s*\(\s*\)|\bApplyPendingMigrations\s*\(", RegexOptions.Compiled);

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "DesignAid.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("リポジトリのルートが見つかりません");
    }

    /// <summary>登録の前にマイグレーションの経路を通っていないメンバー（メソッド）の先頭行を返す。</summary>
    public static List<string> FindUnmigratedEntries(string source)
    {
        var text = ProjectLockCoverageTests.StripCommentsAndStrings(source);
        var problems = new List<string>();
        foreach (var chunk in MemberStart.Split(text))
        {
            var reg = FactoryRegistration.Match(chunk);
            if (!reg.Success) continue;
            var before = chunk[..reg.Index];
            if (!MigrationPath.IsMatch(before))
                problems.Add(chunk.TrimStart().Split('\n')[0].Trim());
        }
        return problems;
    }

    [Fact]
    public void ファクトリを登録する入口は_登録の前に保留中のマイグレーションの経路を通る()
    {
        var srcRoot = Path.Combine(RepoRoot(), "src", "DesignAid");
        var problems = new List<string>();
        var registrations = 0;
        foreach (var file in Directory.GetFiles(srcRoot, "*.cs", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(srcRoot, file).Replace('\\', '/');
            if (rel.StartsWith("obj/") || rel.StartsWith("bin/") || rel.Contains("Migrations/")) continue;
            var src = File.ReadAllText(file);
            registrations += FactoryRegistration.Matches(ProjectLockCoverageTests.StripCommentsAndStrings(src)).Count;
            foreach (var p in FindUnmigratedEntries(src)) problems.Add($"{rel}: {p}");
        }
        Assert.True(problems.Count == 0, "マイグレーションの経路を通さずにファクトリを登録している入口: " + string.Join(" | ", problems));
        // 走査が空振りでないこと: ダッシュボード・MCP・DI の組み立て（CommandHelper）の 3 つ
        Assert.True(registrations >= 3, $"登録の呼び出しを {registrations} 件しか見つけられない（走査の正規表現が実コードの形から外れた）");
    }

    [Fact]
    public void ダッシュボードは_DB呼び出しの前にCreateDbContextを通す()
    {
        // 呼び出し側で固定する: DashboardCommand が、ファクトリの登録より前に CreateDbContext を呼ぶ
        var src = File.ReadAllText(Path.Combine(RepoRoot(), "src", "DesignAid", "Commands", "Dashboard", "DashboardCommand.cs"));
        var text = ProjectLockCoverageTests.StripCommentsAndStrings(src);
        var migrate = MigrationPath.Match(text);
        var register = FactoryRegistration.Match(text);
        Assert.True(migrate.Success, "DashboardCommand が CreateDbContext / ApplyPendingMigrations を呼んでいない");
        Assert.True(register.Success);
        Assert.True(migrate.Index < register.Index, "マイグレーションの経路が、ファクトリの登録より後ろにある");
    }

    [Fact]
    public void 検出力_経路を通さない入口を検出し_通す入口は通す()
    {
        const string bad = """
            public class X
            {
                private void Start()
                {
                    builder.Services.AddDesignAidServices(dbPath, dataDir);
                }
            }
            """;
        const string late = """
            public class X
            {
                private void Start()
                {
                    builder.Services.AddDesignAidServices(dbPath, dataDir);
                    using (var ctx = CommandHelper.CreateDbContext()) { }
                }
            }
            """;
        const string good = """
            public class X
            {
                private void Start()
                {
                    using (var ctx = CommandHelper.CreateDbContext()) { }
                    builder.Services.AddDesignAidServices(dbPath, dataDir);
                }
            }
            """;
        const string declaration = """
            public static class Ext
            {
                public static IServiceCollection AddDesignAidServices(this IServiceCollection services, string dbPath)
                {
                    return services;
                }
            }
            """;
        Assert.Single(FindUnmigratedEntries(bad));
        Assert.Single(FindUnmigratedEntries(late)); // 登録の「後」に通しても、登録の時点では未適用
        Assert.Empty(FindUnmigratedEntries(good));
        Assert.Empty(FindUnmigratedEntries(declaration));
    }
}
