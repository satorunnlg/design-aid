using System.Text.RegularExpressions;

namespace DesignAid.Tests.Application;

/// <summary>
/// VectorIndex の行の中身を書き換える箇所が、すべて <c>UpdatedAt</c> を進めていることを、ソースの走査で確かめる静的な検査（#10）。
/// 検索のキャッシュ（hnsw_index.bin）の古さの判定は、件数・最大 Id・Id の合計・<b>最後の更新時刻</b>で行う。
/// <c>UpdatedAt</c> を進めずに中身だけを書き換えると、古いグラフが使われ続ける。
/// <para>
/// 判定は純関数 <see cref="FindViolations"/> に切り出し、合成した違反・適合のソースで検出力を確かめる。
/// </para>
/// </summary>
public class VectorIndexWriteCoverageTests
{
    private static readonly Regex MemberStart = new(@"(?m)^    (?=[^\s{}])", RegexOptions.Compiled);

    /// <summary>VectorIndex の行（<c>VectorIndexEntry</c>）を扱う箇所か。</summary>
    private static readonly Regex TouchesVectorIndex = new(@"\bVectorIndex(Entry)?\b", RegexOptions.Compiled);

    /// <summary>既存の行の中身を書き換える代入（<c>x.Prop = ...</c>。初期化子の <c>Prop = ...</c> は新しい行なので含めない）。</summary>
    private static readonly Regex FieldWrite = new(
        @"\b(?!this\b)\w+\.(PartId|PartNumber|Content|Embedding|Dimensions|AssetId|AssetName|ProjectId|ProjectName|Type|FilePath)\s*=(?!=)",
        RegexOptions.Compiled);

    /// <summary>SQL で直接行を書き換える呼び出し（EF の変更追跡を通らないので、UpdatedAt を自動では進められない）。</summary>
    private static readonly Regex RawWrite = new(
        @"ExecuteUpdate|ExecuteSql|FromSql|ExecuteNonQuery|\bUPDATE\s+VectorIndex", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>行の UpdatedAt を進める代入（<c>x.UpdatedAt = DateTime.UtcNow</c>）。</summary>
    private static readonly Regex AdvancesUpdatedAt = new(@"\b\w+\.UpdatedAt\s*=\s*DateTime\.UtcNow", RegexOptions.Compiled);

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "DesignAid.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("リポジトリのルートが見つかりません");
    }

    /// <summary>
    /// 1 つのソースから、UpdatedAt を進めずに VectorIndex の行を書き換えているメンバー（メソッド）の先頭行を返す。
    /// </summary>
    public static List<string> FindViolations(string source)
    {
        var text = ProjectLockCoverageTests.StripCommentsAndStrings(source);
        var violations = new List<string>();
        foreach (var chunk in MemberStart.Split(text))
        {
            if (!TouchesVectorIndex.IsMatch(chunk)) continue;
            var writes = FieldWrite.IsMatch(chunk) || RawWrite.IsMatch(chunk);
            if (writes && !AdvancesUpdatedAt.IsMatch(chunk))
                violations.Add(chunk.TrimStart().Split('\n')[0].Trim());
        }
        return violations;
    }

    [Fact]
    public void VectorIndexの行を書き換える箇所は_すべてUpdatedAtを進めている()
    {
        var srcRoot = Path.Combine(RepoRoot(), "src", "DesignAid");
        var problems = new List<string>();
        foreach (var file in Directory.GetFiles(srcRoot, "*.cs", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(srcRoot, file).Replace('\\', '/');
            if (rel.StartsWith("Migrations/") || rel.Contains("/Migrations/") || rel.StartsWith("obj/") || rel.StartsWith("bin/")) continue;
            foreach (var v in FindViolations(File.ReadAllText(file))) problems.Add($"{rel}: {v}");
        }
        Assert.True(problems.Count == 0, "UpdatedAt を進めずに VectorIndex の行を書き換えている箇所: " + string.Join(" | ", problems));
    }

    [Fact]
    public void 走査が実在の書き換え箇所を見ている_VectorSearchServiceとAssetRenameService()
    {
        // 走査が空振り（何も見ていない）でないこと: 書き換え代入を含むメンバーが、実際に 3 つ以上見つかる
        var srcRoot = Path.Combine(RepoRoot(), "src", "DesignAid");
        var seen = 0;
        foreach (var rel in new[] { "Infrastructure/VectorSearch/VectorSearchService.cs", "Application/Services/AssetRenameService.cs" })
        {
            var text = ProjectLockCoverageTests.StripCommentsAndStrings(File.ReadAllText(Path.Combine(srcRoot, rel)));
            seen += MemberStart.Split(text).Count(c => TouchesVectorIndex.IsMatch(c) && FieldWrite.IsMatch(c));
        }
        Assert.True(seen >= 3, $"書き換え箇所を {seen} 件しか見つけられない（走査の正規表現が実コードの形から外れた）");
    }

    [Fact]
    public void 検出力_UpdatedAtを進めない書き換えを違反として検出する_進める書き換えは通す()
    {
        const string bad = """
            public class X
            {
                private async Task Rename(Db db)
                {
                    var rows = await db.VectorIndex.ToListAsync();
                    foreach (var v in rows) { v.AssetName = "n"; }
                }
            }
            """;
        const string good = """
            public class X
            {
                private async Task Rename(Db db)
                {
                    var rows = await db.VectorIndex.ToListAsync();
                    foreach (var v in rows) { v.AssetName = "n"; v.UpdatedAt = DateTime.UtcNow; }
                }
            }
            """;
        const string rawSql = """
            public class X
            {
                private async Task Rename(Db db)
                {
                    await db.VectorIndex.Where(v => v.Id == 1).ExecuteUpdateAsync(s => s.SetProperty(v => v.AssetName, "n"));
                }
            }
            """;
        const string initializerOnly = """
            public class X
            {
                private void Add(Db db)
                {
                    db.VectorIndex.Add(new VectorIndexEntry { AssetName = "n", UpdatedAt = DateTime.UtcNow });
                }
            }
            """;
        Assert.Single(FindViolations(bad));
        Assert.Empty(FindViolations(good));
        Assert.Single(FindViolations(rawSql));
        Assert.Empty(FindViolations(initializerOnly)); // 新しい行の追加は書き換えではない
    }
}
