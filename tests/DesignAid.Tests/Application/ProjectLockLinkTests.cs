using System.Diagnostics;
using DesignAid.Application.Services;

namespace DesignAid.Tests.Application;

/// <summary>
/// ProjectLock の再入の判定が、junction・symlink で指した同じ場所を「同じ場所」と見なすことの検査（#10）。
/// リンクの作り方は OS で違う（Windows は管理者権限が要らない junction、Unix は symlink）ので、
/// それぞれの OS で作れるものを使う。実データには触れない（すべて一時ディレクトリ）。
/// </summary>
public class ProjectLockLinkTests : IDisposable
{
    private readonly string _base = Path.Combine(Path.GetTempPath(), "design-aid-lock-link-tests", Guid.NewGuid().ToString());

    public ProjectLockLinkTests() => Directory.CreateDirectory(_base);

    public void Dispose()
    {
        // リンクの先を巻き込まないよう、リンクを先に外す
        foreach (var d in Directory.GetDirectories(_base))
        {
            try { if (new DirectoryInfo(d).LinkTarget != null) Directory.Delete(d); } catch { }
        }
        try { Directory.Delete(_base, true); } catch { }
        GC.SuppressFinalize(this);
    }

    /// <summary>ディレクトリへのリンク（Windows は junction、Unix は symlink）を作る。</summary>
    private static void CreateDirLink(string link, string target)
    {
        if (OperatingSystem.IsWindows())
        {
            var psi = new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
            {
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true
            };
            using var p = Process.Start(psi)!;
            p.StandardOutput.ReadToEnd();
            var err = p.StandardError.ReadToEnd();
            p.WaitForExit();
            Assert.True(p.ExitCode == 0, $"junction を作れない: {err}");
        }
        else
        {
            Directory.CreateSymbolicLink(link, target);
        }
    }

    private string MakeReal(string name)
    {
        var dir = Path.Combine(_base, name);
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void ResolveKey_リンクと実体は同じキーになる()
    {
        var real = MakeReal("real");
        var link = Path.Combine(_base, "link");
        CreateDirLink(link, real);
        Assert.Equal(ProjectLock.ResolveKey(real), ProjectLock.ResolveKey(link));
    }

    [Fact]
    public void ResolveKey_途中の階層がリンクでも_実体のキーになる()
    {
        var real = MakeReal("real2");
        var project = Path.Combine(real, "project");
        Directory.CreateDirectory(project);
        var link = Path.Combine(_base, "link2");
        CreateDirLink(link, real);
        // link2/project の「link2」の部分がリンク（最後の階層ではない）
        Assert.Equal(ProjectLock.ResolveKey(project), ProjectLock.ResolveKey(Path.Combine(link, "project")));
    }

    [Fact]
    public async Task 再入_実体で持っている流れの中でリンク経由で取る_同じ場所と見なして再入になる()
    {
        var real = MakeReal("real3");
        var link = Path.Combine(_base, "link3");
        CreateDirLink(link, real);

        await Task.Run(() =>
        {
            using var outer = ProjectLock.TryAcquire(real);
            Assert.NotNull(outer);
            using var inner = ProjectLock.TryAcquire(link);
            Assert.True(inner != null && inner.IsReentrant, "リンク経由の同じ場所が再入と判定されず、自分自身のロックに止められた");
        });
    }

    [Fact]
    public async Task 再入_リンク経由で持っている流れの中で実体で取る_再入になる()
    {
        var real = MakeReal("real4");
        var link = Path.Combine(_base, "link4");
        CreateDirLink(link, real);

        await Task.Run(() =>
        {
            using var outer = ProjectLock.TryAcquire(link);
            Assert.NotNull(outer);
            using var inner = ProjectLock.TryAcquire(real);
            Assert.True(inner != null && inner.IsReentrant);
        });
    }

    [Fact]
    public async Task 排他_リンク経由でも実体と同じロックファイルを使う_別の流れは取れない()
    {
        var real = MakeReal("real5");
        var link = Path.Combine(_base, "link5");
        CreateDirLink(link, real);

        var acquired = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var holder = Task.Run(async () =>
        {
            using var h = ProjectLock.TryAcquire(link);
            Assert.NotNull(h);
            acquired.SetResult();
            await release.Task;
        });
        await acquired.Task;

        // 別の流れ（保持している流れの AsyncLocal を引き継がない）から、実体で取ろうとしても取れない
        Assert.Null(ProjectLock.TryAcquire(real));

        release.SetResult();
        await holder;
    }

    [Fact]
    public void ResolveKey_存在しない場所_リンクでない場所は_例外にせずそのパスを返す()
    {
        // 一時フォルダ自体がリンクの下にある OS がある（macOS の /var は /private/var へのリンク）。
        // そのため、期待値は「土台の実体のパス」を基準に組み立てる
        var baseKey = ProjectLock.ResolveKey(_base);
        var missing = Path.Combine(_base, "no", "such", "dir");
        Assert.Equal(Path.Combine(baseKey, "no", "such", "dir"), ProjectLock.ResolveKey(missing));
        var real = MakeReal("plain");
        Assert.Equal(Path.Combine(baseKey, "plain"), ProjectLock.ResolveKey(real));
    }

    [Fact]
    public void ResolveKey_循環したリンクでも_止まらず例外にしない()
    {
        if (OperatingSystem.IsWindows()) return; // 循環する junction は作れない（symlink の循環は Unix で確かめる）
        var a = Path.Combine(_base, "a");
        var b = Path.Combine(_base, "b");
        Directory.CreateSymbolicLink(a, b);
        Directory.CreateSymbolicLink(b, a);
        var key = ProjectLock.ResolveKey(a);
        Assert.False(string.IsNullOrEmpty(key));
    }

    [Fact]
    public void TrimTrailingSeparators_ルートは保ち_通常の末尾の区切りは除く()
    {
        var sep = Path.DirectorySeparatorChar;
        var root = Path.GetPathRoot(_base)!;
        Assert.Equal(root, ProjectLock.TrimTrailingSeparators(root));
        Assert.Equal(_base, ProjectLock.TrimTrailingSeparators(_base + sep));
        Assert.Equal(_base, ProjectLock.TrimTrailingSeparators(_base + sep + sep));
    }

    [Fact]
    public void ResolveKey_ルートを指すリンクの先の階層も_絶対パスのまま実体と同じキーになる()
    {
        if (OperatingSystem.IsWindows()) return; // ルートへの symlink は Unix だけ
        var rootLink = Path.Combine(_base, "rootlink");
        Directory.CreateSymbolicLink(rootLink, "/");
        var viaLink = rootLink + _base; // 例: /tmp/x/rootlink/tmp/x
        var key = ProjectLock.ResolveKey(viaLink);
        Assert.True(Path.IsPathRooted(key), key);
        Assert.Equal(ProjectLock.ResolveKey(_base), key);
        Assert.Equal("/", ProjectLock.ResolveKey(rootLink));
    }

    [Theory]
    [InlineData("Proj", new[] { "proj", "other" }, "proj")]          // 大文字小文字だけ違う 1 つ → 実際の綴り
    [InlineData("Proj", new[] { "Proj", "proj" }, "Proj")]           // 完全一致を優先
    [InlineData("proj", new[] { "Proj", "PROJ" }, "proj")]           // 2 つ以上あって完全一致が無い → そのまま
    [InlineData("proj", new[] { "other" }, "proj")]                  // 無い → そのまま
    [InlineData("proj", new string[0], "proj")]
    public void PickActualName_完全一致を優先_大小文字違いは一意のときだけ採る(string name, string[] siblings, string expected)
        => Assert.Equal(expected, ProjectLock.PickActualName(name, siblings));

    /// <summary>この場所が大文字小文字を区別しないボリュームか（実際にファイルを作って確かめる）。</summary>
    private bool IsCaseInsensitiveVolume()
    {
        var probe = Path.Combine(_base, "casepROBE");
        File.WriteAllText(probe, "x");
        try { return File.Exists(Path.Combine(_base, "CASEprobe")); }
        finally { File.Delete(probe); }
    }

    [Fact]
    public void 再入の判定_大文字小文字を区別しないボリュームでは_綴りを変えて同じ実体を指しても再入になる()
    {
        if (!IsCaseInsensitiveVolume()) return; // 区別するボリュームでは別の実体になる（下の検査）
        var dir = MakeReal("CaseProj");
        Assert.Equal(ProjectLock.ResolveKey(dir), ProjectLock.ResolveKey(Path.Combine(_base, "caseproj")));
        Assert.Equal(ProjectLock.ResolveKey(dir), ProjectLock.ResolveKey(Path.Combine(_base, "CASEPROJ")));
        using var outer = ProjectLock.TryAcquire(dir);
        Assert.NotNull(outer);
        foreach (var variant in new[] { Path.Combine(_base, "caseproj"), Path.Combine(_base, "CASEPROJ"), dir.ToUpperInvariant(), dir.ToLowerInvariant() })
        {
            using var inner = ProjectLock.TryAcquire(variant);
            Assert.True(inner != null && inner.IsReentrant, variant);
        }
    }

    [Fact]
    public void 再入の判定_大文字小文字を区別するボリュームでは_綴りだけ違う別の実体は再入にならない()
    {
        if (IsCaseInsensitiveVolume()) return; // 区別しないボリュームでは同じ実体（上の検査）
        var upper = MakeReal("SepProj");
        var lower = MakeReal("sepproj");
        Assert.NotEqual(ProjectLock.ResolveKey(upper), ProjectLock.ResolveKey(lower));
        using var outer = ProjectLock.TryAcquire(upper);
        Assert.NotNull(outer);
        using var inner = ProjectLock.TryAcquire(lower);
        Assert.NotNull(inner);
        Assert.False(inner!.IsReentrant); // 別の実体のロックを、自分のロックの再入として素通りしない
    }

}
