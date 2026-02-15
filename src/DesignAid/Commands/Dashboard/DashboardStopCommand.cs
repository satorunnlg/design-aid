using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
using System.Text.Json;

namespace DesignAid.Commands.Dashboard;

/// <summary>
/// ダッシュボードを停止するコマンド。
/// daid dashboard stop
/// </summary>
public class DashboardStopCommand : Command
{
    public DashboardStopCommand() : base("stop", "ダッシュボードを停止")
    {
        this.Handler = CommandHandler.Create(StopAsync);
    }

    private static async Task<int> StopAsync()
    {
        if (CommandHelper.EnsureDataDirectory() == null) return 3;
        var pidPath = CommandHelper.GetDashboardPidPath();

        if (!File.Exists(pidPath))
        {
            Console.Error.WriteLine("ダッシュボードは実行されていません");
            return 1;
        }

        DashboardPidInfo? pidInfo;
        try
        {
            var json = await File.ReadAllTextAsync(pidPath);
            pidInfo = JsonSerializer.Deserialize<DashboardPidInfo>(json);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"PID ファイルの読み込みに失敗しました: {ex.Message}");
            return 1;
        }

        if (pidInfo == null)
        {
            Console.Error.WriteLine("PID ファイルが不正です");
            File.Delete(pidPath);
            return 1;
        }

        // shutdown エンドポイントに POST
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var response = await client.PostAsync(
                $"http://localhost:{pidInfo.Port}/api/shutdown", null);

            if (response.IsSuccessStatusCode)
            {
                Console.WriteLine($"ダッシュボードを停止しました (PID: {pidInfo.Pid})");

                // PID ファイルが削除されるまで少し待つ
                for (int i = 0; i < 10 && File.Exists(pidPath); i++)
                {
                    await Task.Delay(500);
                }

                return 0;
            }
            else
            {
                Console.Error.WriteLine($"停止リクエストに失敗しました: HTTP {response.StatusCode}");
            }
        }
        catch (HttpRequestException)
        {
            Console.Error.WriteLine("ダッシュボードに接続できません。プロセスが既に終了している可能性があります。");
        }
        catch (TaskCanceledException)
        {
            Console.Error.WriteLine("接続がタイムアウトしました");
        }

        // フォールバック: プロセスを直接終了
        try
        {
            var process = System.Diagnostics.Process.GetProcessById(pidInfo.Pid);
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                Console.WriteLine($"プロセスを強制終了しました (PID: {pidInfo.Pid})");
            }
        }
        catch (ArgumentException)
        {
            Console.WriteLine("プロセスは既に終了しています");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"プロセスの終了に失敗しました: {ex.Message}");
            return 1;
        }

        // PID ファイルを削除
        try { File.Delete(pidPath); } catch { }

        return 0;
    }
}
