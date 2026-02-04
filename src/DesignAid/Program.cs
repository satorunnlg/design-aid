using System.CommandLine;
using DesignAid.Commands;
using DesignAid.Commands.Project;
using DesignAid.Commands.Asset;
using DesignAid.Commands.Part;

namespace DesignAid;

/// <summary>
/// Design Aid CLI エントリーポイント
/// </summary>
public class Program
{
    public static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("Design Aid - 機械設計支援システム")
        {
            // 初期化・設定
            new SetupCommand(),
            new ConfigCommand(),

            // プロジェクト・装置・パーツ管理
            new ProjectCommand(),
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

            // バックアップ
            new BackupCommand()
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

        return await rootCommand.InvokeAsync(args);
    }
}
