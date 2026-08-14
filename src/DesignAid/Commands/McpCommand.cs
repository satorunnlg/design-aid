using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using DesignAid.Configuration;

namespace DesignAid.Commands;

/// <summary>
/// MCP サーバーモードで起動するコマンド。
/// stdin/stdout の JSON-RPC 2.0 通信で MCP プロトコルを提供する。
/// </summary>
public class McpCommand : Command
{
    public McpCommand() : base("mcp", "MCP サーバーモードで起動（Claude Code / VS Code 連携）")
    {
        this.Handler = CommandHandler.Create(Execute);
    }

    private static async Task Execute()
    {
        var dataDir = CommandHelper.GetDataDirectory();
        if (dataDir == null)
        {
            Console.Error.WriteLine("[ERROR] プロジェクトが見つかりません。");
            Environment.ExitCode = 3;
            return;
        }

        var dbPath = CommandHelper.GetDatabasePath();

        var builder = Host.CreateApplicationBuilder();

        // MCP サーバーではログを stderr に出力（stdout は MCP 通信専用）
        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Logging.AddConsole(options =>
        {
            options.LogToStandardErrorThreshold = LogLevel.Trace;
        });

        // DB マイグレーション実行（テーブルが存在することを保証）
        using (var ctx = CommandHelper.CreateDbContext()) { }

        // Design Aid コアサービスを DI 登録
        builder.Services.AddDesignAidServices(dbPath, dataDir);

        // MCP サーバーを登録（STDIO トランスポート + ツール自動検出）
        builder.Services
            .AddMcpServer(options =>
            {
                options.ServerInfo = new()
                {
                    Name = "design-aid",
                    Version = "0.5.5-alpha"
                };
            })
            .WithStdioServerTransport()
            .WithToolsFromAssembly();

        await builder.Build().RunAsync();
    }
}
