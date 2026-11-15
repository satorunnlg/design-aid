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
        var missing = Path.Combine(_base, "no", "such", "dir");
        Assert.Equal(Path.GetFullPath(missing), ProjectLock.ResolveKey(missing));
        var real = MakeReal("plain");
        Assert.Equal(Path.GetFullPath(real), ProjectLock.ResolveKey(real));
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
}
