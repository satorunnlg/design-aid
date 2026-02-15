using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MudBlazor.Services;
using DesignAid.Configuration;
using DesignAid.Dashboard.Services;

namespace DesignAid.Commands.Dashboard;

/// <summary>
/// Web ダッシュボードを起動するコマンド。
/// daid dashboard [--port 5180] [--no-browser]
/// </summary>
public class DashboardCommand : Command
{
    public DashboardCommand() : base("dashboard", "Web ダッシュボードの管理")
    {
        this.Add(new Option<int>("--port", () => 5180, "ポート番号"));
        this.Add(new Option<bool>("--no-browser", "ブラウザを自動で開かない"));

        this.Add(new DashboardStopCommand());

        this.Handler = CommandHandler.Create<int, bool>(StartAsync);
    }

    private static async Task<int> StartAsync(int port, bool noBrowser)
    {
        var dataDir = CommandHelper.EnsureDataDirectory();
        if (dataDir == null) return 3;
        var dbPath = CommandHelper.GetDatabasePath();
        var pidPath = CommandHelper.GetDashboardPidPath();

        // 既に起動中かチェック
        if (File.Exists(pidPath))
        {
            try
            {
                var existingInfo = JsonSerializer.Deserialize<DashboardPidInfo>(
                    await File.ReadAllTextAsync(pidPath));
                if (existingInfo != null)
                {
                    try
                    {
                        var existingProcess = Process.GetProcessById(existingInfo.Pid);
                        if (!existingProcess.HasExited)
                        {
                            Console.Error.WriteLine($"ダッシュボードは既に起動しています (PID: {existingInfo.Pid}, ポート: {existingInfo.Port})");
                            Console.Error.WriteLine($"停止するには: daid dashboard stop");
                            return 1;
                        }
                    }
                    catch (ArgumentException)
                    {
                        // プロセスが存在しない場合は PID ファイルを削除
                    }
                }
            }
            catch
            {
                // PID ファイルの読み込みに失敗した場合は無視
            }
            File.Delete(pidPath);
        }

        // DB の存在チェック
        if (!File.Exists(dbPath))
        {
            Console.Error.WriteLine("[ERROR] データベースが見つかりません");
            Console.Error.WriteLine("  先に 'daid setup' を実行してください");
            return 1;
        }

        var url = $"http://localhost:{port}";

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls(url);

        // Blazor Server サービス
        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents();

        // MudBlazor
        builder.Services.AddMudServices();

        // Design Aid コアサービス
        builder.Services.AddDesignAidServices(dbPath, dataDir);

        // ダッシュボード専用サービス
        builder.Services.AddScoped<DashboardService>();

        // ログ設定（ダッシュボードモードでは最小限）
        builder.Logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Warning);

        var app = builder.Build();

        app.UseStaticFiles();
        app.UseAntiforgery();

        app.MapRazorComponents<DesignAid.Dashboard.Components.App>()
            .AddInteractiveServerRenderMode();

        // シャットダウン用エンドポイント
        app.MapPost("/api/shutdown", (IHostApplicationLifetime lifetime) =>
        {
            lifetime.StopApplication();
            return Results.Ok(new { message = "シャットダウン中..." });
        });

        // PID ファイルを書き込み
        var pidInfo = new DashboardPidInfo
        {
            Pid = Environment.ProcessId,
            Port = port,
            StartedAt = DateTime.UtcNow.ToString("o")
        };
        await File.WriteAllTextAsync(pidPath,
            JsonSerializer.Serialize(pidInfo, new JsonSerializerOptions { WriteIndented = true }));

        // Graceful shutdown 時に PID ファイルを削除
        var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
        lifetime.ApplicationStopping.Register(() =>
        {
            try { File.Delete(pidPath); } catch { }
        });

        Console.WriteLine($"Design Aid ダッシュボード起動中...");
        Console.WriteLine($"  URL: {url}");
        Console.WriteLine($"  PID: {Environment.ProcessId}");
        Console.WriteLine($"  停止: Ctrl+C または 'daid dashboard stop'");

        // ブラウザを開く
        if (!noBrowser)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch
            {
                // ブラウザの起動に失敗しても続行
            }
        }

        await app.RunAsync();
        return 0;
    }
}

/// <summary>
/// ダッシュボードの PID 情報。
/// </summary>
public class DashboardPidInfo
{
    public int Pid { get; set; }
    public int Port { get; set; }
    public string StartedAt { get; set; } = string.Empty;
}
