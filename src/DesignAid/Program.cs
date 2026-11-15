using System.CommandLine;
using System.Text;
using DesignAid.Commands;
using DesignAid.Commands.Archive;
using DesignAid.Commands.Asset;
using DesignAid.Commands.Dashboard;
using DesignAid.Commands.Part;

namespace DesignAid;

/// <summary>
/// Design Aid CLI エントリーポイント
/// </summary>
public class Program
{
    public static async Task<int> Main(string[] args)
    {
        // コンソール出力を UTF-8 に設定
        Console.OutputEncoding = Encoding.UTF8;
        return await RunAsync(args);
    }

    /// <summary>
    /// コマンドラインを実行して終了コードを返す（<see cref="Main"/> の本体。検査から Main と同じ経路を通せるよう分けてある）。
    /// <c>Environment.ExitCode</c> を設定するだけで戻り値を返さないハンドラの失敗も、終了コードに反映する（#8）。
    /// </summary>
    public static async Task<int> RunAsync(string[] args)
    {
        var rootCommand = BuildRootCommand();
        return ResolveExitCode(await rootCommand.InvokeAsync(args), Environment.ExitCode);
    }

    /// <summary>
    /// 全コマンドを組み立てる（検査から全コマンドを列挙できるよう、Main から分けてある）。
    /// </summary>
    public static RootCommand BuildRootCommand()
    {
        var rootCommand = new RootCommand("Design Aid - 機械設計支援システム")
        {
            // 初期化・設定
            new SetupCommand(),
            new ConfigCommand(),

            // 装置・パーツ管理
            new AssetCommand(),
            new PartCommand(),

            // コア機能
            new CheckCommand(),
            new VerifyCommand(),
            new SyncCommand(),
            new StatusCommand(),

            // 手配・検索
            new DeployCommand(),
            new SearchCommand(),

            // バックアップ・復元
            new BackupCommand(),
            new RestoreCommand(),

            // アーカイブ（容量節約）
            new ArchiveCommand(),

            // ダッシュボード
            new DashboardCommand(),

            // ツール管理
            new UpdateCommand(),

            // MCP サーバー（Claude Code / VS Code 連携）
            new McpCommand()
        };

        // バージョン表示
        rootCommand.Description = """
            Design Aid (DA) - 機械設計支援システム

            設計の論理（理）と物理的な手配の乖離を埋めるためのサポートシステム。
            手配境界を抽象化の基準として、設計の整合性と知見の継承を支援します。

            設計哲学:
              - Support, Not Control: 設計者を縛るのではなく「助ける」存在
              - Procurement Boundary: 手配タイミングをシステムの境界とする
              - Hash-Based Integrity: 成果物をハッシュ値で管理し不整合を検知
            """;

        return rootCommand;
    }

    /// <summary>
    /// 終了コードを決める（純関数）。<c>InvokeAsync</c> の戻り値が 0 のときは、<c>Environment.ExitCode</c> を採る。
    /// <c>SetHandler(async ...)</c> のように <c>Environment.ExitCode</c> を設定するだけのハンドラは、失敗しても
    /// <c>InvokeAsync</c> が 0 を返すため（#8）。戻り値が 0 以外ならそれを優先する。
    /// </summary>
    public static int ResolveExitCode(int invokeResult, int environmentExitCode)
        => invokeResult != 0 ? invokeResult : environmentExitCode;
}
