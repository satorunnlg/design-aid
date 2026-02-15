using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
using DesignAid.Application.Services;
using DesignAid.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DesignAid.Commands;

/// <summary>
/// da setup - データディレクトリの初期化コマンド。
/// </summary>
public class SetupCommand : Command
{
    public SetupCommand() : base("setup", "データディレクトリを初期化する")
    {
        var pathOption = new Option<string?>(
            aliases: ["--path", "-p"],
            description: "初期化するデータディレクトリのパス（省略時: カレントディレクトリの data/）");

        var forceOption = new Option<bool>(
            aliases: ["--force", "-f"],
            description: "既存の設定を上書きする");

        AddOption(pathOption);
        AddOption(forceOption);

        Handler = CommandHandler.Create<string?, bool>(ExecuteAsync);
    }

    private async Task<int> ExecuteAsync(string? path, bool force)
    {
        try
        {
            // データディレクトリのパスを決定
            var dataDir = string.IsNullOrEmpty(path)
                ? Path.Combine(Directory.GetCurrentDirectory(), "data")
                : Path.GetFullPath(path);

            Console.WriteLine($"データディレクトリを初期化します: {dataDir}");
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
                    Console.WriteLine($"  [作成] {Path.GetRelativePath(dataDir, dir)}/");
                }
                else
                {
                    Console.WriteLine($"  [存在] {Path.GetRelativePath(dataDir, dir)}/");
                }
            }

            // .gitignore の作成（DB ファイルを除外）
            var gitignorePath = Path.Combine(dataDir, ".gitignore");
            if (!File.Exists(gitignorePath) || force)
            {
                var gitignoreContent = """
                    # Design Aid データディレクトリ用 .gitignore

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
                    """;

                await File.WriteAllTextAsync(gitignorePath, gitignoreContent);
                Console.WriteLine($"  [作成] .gitignore");
            }
            else
            {
                Console.WriteLine($"  [存在] .gitignore");
            }

            // SQLite データベースの初期化（マイグレーション適用）
            var dbPath = Path.Combine(dataDir, "design_aid.db");
            try
            {
                var optionsBuilder = new DbContextOptionsBuilder<DesignAidDbContext>();
                optionsBuilder.UseSqlite($"Data Source={dbPath}");
                using var context = new DesignAidDbContext(optionsBuilder.Options);
                context.Database.Migrate();
                Console.WriteLine($"  [作成] design_aid.db（マイグレーション適用）");

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
}
