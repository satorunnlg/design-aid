using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
using DesignAid.Application.Services;
using DesignAid.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DesignAid.Commands;

/// <summary>
/// daid setup - プロジェクトディレクトリの初期化コマンド。
/// 名前を指定した場合はディレクトリを作成し、省略時はカレントディレクトリを初期化する。
/// </summary>
public class SetupCommand : Command
{
    public SetupCommand() : base("setup", "プロジェクトディレクトリを初期化する")
    {
        var nameArgument = new Argument<string?>(
            name: "name",
            description: "プロジェクト名（ディレクトリ名）。省略時はカレントディレクトリを初期化",
            getDefaultValue: () => null);

        var forceOption = new Option<bool>(
            aliases: ["--force", "-f"],
            description: "既存の設定を上書きする");

        AddArgument(nameArgument);
        AddOption(forceOption);

        Handler = CommandHandler.Create<string?, bool>(ExecuteAsync);
    }

    private async Task<int> ExecuteAsync(string? name, bool force)
    {
        try
        {
            // プロジェクトルートのパスを決定
            string dataDir;
            if (string.IsNullOrEmpty(name))
            {
                // 名前なし: カレントディレクトリをプロジェクトルートとして初期化
                dataDir = Directory.GetCurrentDirectory();
            }
            else
            {
                // 名前あり: カレントディレクトリ内にディレクトリを作成
                dataDir = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), name));
            }

            // 既に初期化済みかチェック
            var dbPath = Path.Combine(dataDir, CommandHelper.DatabaseFileName);
            if (File.Exists(dbPath) && !force)
            {
                Console.Error.WriteLine($"[ERROR] 既に初期化されています: {dataDir}");
                Console.Error.WriteLine("  上書きするには --force オプションを使用してください。");
                return 1;
            }

            Console.WriteLine($"プロジェクトディレクトリを初期化します: {dataDir}");
            Console.WriteLine();

            // ディレクトリ構造の作成
            var directories = new[]
            {
                dataDir,
                Path.Combine(dataDir, "assets"),
                Path.Combine(dataDir, "components")
            };

            foreach (var dir in directories)
            {
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                    Console.WriteLine($"  [作成] {GetRelativeDisplay(dataDir, dir)}/");
                }
                else
                {
                    Console.WriteLine($"  [存在] {GetRelativeDisplay(dataDir, dir)}/");
                }
            }

            // .gitignore の作成（DB ファイルを除外）
            var gitignorePath = Path.Combine(dataDir, ".gitignore");
            if (!File.Exists(gitignorePath) || force)
            {
                var gitignoreContent = """
                    # Design Aid プロジェクト用 .gitignore

                    # SQLite データベース（ローカル環境固有）
                    *.db
                    *.db-shm
                    *.db-wal

                    # HNSW インデックスキャッシュ（再構築可能）
                    hnsw_index.bin
                    *.hnsw

                    # バックアップファイル
                    *.zip
                    *.backup

                    # 一時ファイル
                    *.tmp

                    # ダッシュボード PID ファイル
                    .dashboard.pid
                    """;

                await File.WriteAllTextAsync(gitignorePath, gitignoreContent);
                Console.WriteLine($"  [作成] .gitignore");
            }
            else
            {
                Console.WriteLine($"  [存在] .gitignore");
            }

            // SQLite データベースの初期化（マイグレーション適用）
            try
            {
                var optionsBuilder = new DbContextOptionsBuilder<DesignAidDbContext>();
                optionsBuilder.UseSqlite($"Data Source={dbPath}");
                using var context = new DesignAidDbContext(optionsBuilder.Options);
                context.Database.Migrate();
                Console.WriteLine($"  [作成] {CommandHelper.DatabaseFileName}（マイグレーション適用）");

                // 既存の config.json があれば Settings テーブルに移行
                var configJsonPath = Path.Combine(dataDir, "config.json");
                if (File.Exists(configJsonPath))
                {
                    var settingsService = new SettingsService();
                    await settingsService.MigrateFromConfigJsonAsync(context, configJsonPath);
                    Console.WriteLine($"  [移行] config.json → Settings テーブル");
                }

                // デフォルト設定を書き込み（存在しないキーのみ）
                var settings = new SettingsService();
                await settings.SetDefaultsAsync(context);

                if (force)
                {
                    // --force の場合はデフォルト値で上書き
                    foreach (var (key, defaultValue) in SettingsService.Defaults)
                    {
                        if (defaultValue != null)
                        {
                            await settings.SetAsync(context, key, defaultValue);
                        }
                    }
                }

                Console.WriteLine($"  [作成] デフォルト設定（Settings テーブル）");
            }
            catch (Exception dbEx)
            {
                Console.WriteLine($"  [警告] データベースの初期化に失敗しました: {dbEx.Message}");
            }

            Console.WriteLine();
            Console.WriteLine("初期化が完了しました。");
            Console.WriteLine();
            Console.WriteLine("次のステップ:");
            Console.WriteLine($"  1. daid config set <key> <value> で設定を調整");
            Console.WriteLine($"  2. 装置を追加: daid asset add <name>");

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"エラー: {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// プロジェクトルートからの相対パスを表示用に取得する。
    /// ルート自身の場合は "." を返す。
    /// </summary>
    private static string GetRelativeDisplay(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        return relative == "." ? Path.GetFileName(root) : relative;
    }
}
