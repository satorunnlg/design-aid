using System.CommandLine;
using DesignAid.Application.Services;
using DesignAid.Commands;

namespace DesignAid.Tests.Application;

/// <summary>
/// backup が、ZIP を作り終えたあと S3 へのアップロードの<b>前に</b>プロジェクトのロックを解放することの検査（#10）。
/// アップロードは長く、ネットワークの状態次第で終わらないこともある。その間ずっとロックを持つと、
/// 同じプロジェクトの改名・sync などが止まる。アップロードを差し替えて、アップロードの時点でロックが空いていることを確かめる。
/// </summary>
[Collection("CommandEnv")]
public class BackupLockReleaseTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "design-aid-backup-lock-tests", Guid.NewGuid().ToString());
    private readonly string? _savedEnv = Environment.GetEnvironmentVariable("DA_DATA_DIR");

    public BackupLockReleaseTests()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "note.txt"), "データ");
        Environment.SetEnvironmentVariable("DA_DATA_DIR", _root);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("DA_DATA_DIR", _savedEnv);
        try { Directory.Delete(_root, true); } catch { }
        GC.SuppressFinalize(this);
    }

    private static async Task<(int Code, string Err)> RunAsync(Command command, params string[] args)
    {
        var so = Console.Out; var se = Console.Error;
        var errW = new StringWriter();
        Console.SetOut(new StringWriter()); Console.SetError(errW);
        Environment.ExitCode = 0;
        try { return (await command.InvokeAsync(args), errW.ToString()); }
        finally { Console.SetOut(so); Console.SetError(se); }
    }

    [Fact]
    public async Task backup_アップロードの時点では_ロックを解放済み()
    {
        var uploaded = false;
        bool? heldDuringUpload = null;
        bool? reentrantDuringUpload = null;
        var cmd = new BackupCommand
        {
            Uploader = (zip, uri, profile) =>
            {
                uploaded = true;
                Assert.True(File.Exists(zip), "アップロードの時点で ZIP が作られている");
                // 同じ流れ: まだ持っていれば再入（IsReentrant = true）になる。解放済みなら新しく取れる（false）
                using (var h = ProjectLock.TryAcquire(_root))
                {
                    reentrantDuringUpload = h?.IsReentrant;
                }
                // 別の流れ（保持している流れの AsyncLocal を引き継がない）から取れるか
                using (ExecutionContext.SuppressFlow())
                {
                    heldDuringUpload = Task.Run(() =>
                    {
                        using var h = ProjectLock.TryAcquire(_root);
                        return h == null; // null = 取れない = まだ誰かが持っている
                    }).GetAwaiter().GetResult();
                }
                return Task.FromResult(0);
            }
        };

        var (code, err) = await RunAsync(cmd, "--data-path", _root, "--bucket", "test-bucket", "--skip-db");

        Assert.True(uploaded, $"アップロードまで進まなかった: {err}");
        Assert.Equal(0, code);
        Assert.False(reentrantDuringUpload, "アップロードの時点でロックを持ったまま（再入になった）");
        Assert.False(heldDuringUpload, "アップロードの時点で、別の流れからロックを取れない");
        // 終わったあとも残らない
        using var after = ProjectLock.TryAcquire(_root);
        Assert.NotNull(after);
    }

    [Fact]
    public async Task backup_ロックを別の流れが持っている間は_アップロードまで進まず止まる()
    {
        var uploaded = false;
        var acquired = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var holder = Task.Run(async () =>
        {
            using var h = ProjectLock.TryAcquire(_root);
            Assert.NotNull(h);
            acquired.SetResult();
            await release.Task;
        });
        await acquired.Task;

        var cmd = new BackupCommand { Uploader = (_, _, _) => { uploaded = true; return Task.FromResult(0); } };
        var (code, _) = await RunAsync(cmd, "--data-path", _root, "--bucket", "test-bucket", "--skip-db");

        release.SetResult();
        await holder;
        Assert.False(uploaded);
        Assert.NotEqual(0, code);
    }
}
