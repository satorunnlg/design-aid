using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
using System.Text.Json;
using DesignAid.Application.Services;
using DesignAid.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DesignAid.Commands;

/// <summary>
/// da config - 設定の表示・変更コマンド。
/// </summary>
public class ConfigCommand : Command
{
    public ConfigCommand() : base("config", "設定の表示・変更")
    {
        // サブコマンド
        AddCommand(new ConfigShowCommand());
        AddCommand(new ConfigSetCommand());
        AddCommand(new ConfigPathCommand());
    }
}

/// <summary>
/// da config show - 現在の設定を表示。
/// </summary>
public class ConfigShowCommand : Command
{
    public ConfigShowCommand() : base("show", "現在の設定を表示する")
    {
        var jsonOption = new Option<bool>(
            aliases: ["--json"],
            description: "JSON 形式で出力");

        AddOption(jsonOption);

        Handler = CommandHandler.Create<bool>(ExecuteAsync);
    }

    private async Task<int> ExecuteAsync(bool json)
    {
        try
        {
            if (CommandHelper.EnsureDataDirectory() == null) return 3;
            var dbPath = CommandHelper.GetDatabasePath();

            if (!File.Exists(dbPath))
            {
                Console.Error.WriteLine($"データベースが見つかりません: {dbPath}");
                Console.Error.WriteLine("daid setup を実行してプロジェクトを初期化してください。");
                return 1;
            }

            var optionsBuilder = new DbContextOptionsBuilder<DesignAidDbContext>();
            optionsBuilder.UseSqlite($"Data Source={dbPath}");
            using var context = new DesignAidDbContext(optionsBuilder.Options);

            var settingsService = new SettingsService();
            var allSettings = await settingsService.GetAllAsync(context);

            if (json)
            {
                var jsonOutput = JsonSerializer.Serialize(allSettings, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                Console.WriteLine(jsonOutput);
            }
            else
            {
                Console.WriteLine("Design Aid 設定");
                Console.WriteLine($"  データベース: {dbPath}");
                Console.WriteLine();
                Console.WriteLine("データベース:");
                Console.WriteLine($"  パス: {allSettings.GetValueOrDefault("database.path")}");
                Console.WriteLine();
                Console.WriteLine("ベクトル検索:");
                Console.WriteLine($"  有効: {allSettings.GetValueOrDefault("vector_search.enabled")}");
                Console.WriteLine($"  HNSW インデックス: {allSettings.GetValueOrDefault("vector_search.hnsw_index_path")}");
                Console.WriteLine();
                Console.WriteLine("埋め込み:");
                Console.WriteLine($"  プロバイダー: {allSettings.GetValueOrDefault("embedding.provider")}");
                Console.WriteLine($"  次元数: {allSettings.GetValueOrDefault("embedding.dimensions")}");
                var model = allSettings.GetValueOrDefault("embedding.model");
                if (!string.IsNullOrEmpty(model))
                    Console.WriteLine($"  モデル: {model}");
                var endpoint = allSettings.GetValueOrDefault("embedding.endpoint");
                if (!string.IsNullOrEmpty(endpoint))
                    Console.WriteLine($"  エンドポイント: {endpoint}");
                var apiKey = allSettings.GetValueOrDefault("embedding.api_key");
                if (!string.IsNullOrEmpty(apiKey))
                    Console.WriteLine($"  API キー: {MaskValue(apiKey)}");
                Console.WriteLine();
                Console.WriteLine("ハッシュ:");
                Console.WriteLine($"  アルゴリズム: {allSettings.GetValueOrDefault("hashing.algorithm")}");
                Console.WriteLine();
                Console.WriteLine("バックアップ:");
                var s3Bucket = allSettings.GetValueOrDefault("backup.s3_bucket");
                Console.WriteLine($"  S3 バケット: {(string.IsNullOrEmpty(s3Bucket) ? "(未設定)" : s3Bucket)}");
                Console.WriteLine($"  S3 プレフィックス: {allSettings.GetValueOrDefault("backup.s3_prefix")}");
                Console.WriteLine($"  AWS プロファイル: {allSettings.GetValueOrDefault("backup.aws_profile")}");
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"エラー: {ex.Message}");
            return 1;
        }
    }

    private static string MaskValue(string value)
    {
        if (value.Length <= 4) return "****";
        return value[..4] + new string('*', Math.Min(value.Length - 4, 20));
    }
}

/// <summary>
/// da config set - 設定値を変更。
/// </summary>
public class ConfigSetCommand : Command
{
    public ConfigSetCommand() : base("set", "設定値を変更する")
    {
        var keyArgument = new Argument<string>("key", "設定キー（例: vector_search.enabled, embedding.provider）");
        var valueArgument = new Argument<string>("value", "設定値");

        AddArgument(keyArgument);
        AddArgument(valueArgument);

        Handler = CommandHandler.Create<string, string>(ExecuteAsync);
    }

    private async Task<int> ExecuteAsync(string key, string value)
    {
        try
        {
            if (CommandHelper.EnsureDataDirectory() == null) return 3;
            var dbPath = CommandHelper.GetDatabasePath();

            if (!File.Exists(dbPath))
            {
                Console.Error.WriteLine($"データベースが見つかりません: {dbPath}");
                Console.Error.WriteLine("daid setup を実行してプロジェクトを初期化してください。");
                return 1;
            }

            // キーのバリデーション
            if (!SettingsService.IsKnownKey(key))
            {
                Console.Error.WriteLine($"不明な設定キー: {key}");
                Console.Error.WriteLine();
                Console.Error.WriteLine("利用可能なキー:");
                foreach (var knownKey in SettingsService.GetKnownKeys())
                {
                    Console.Error.WriteLine($"  {knownKey}");
                }
                return 1;
            }

            var optionsBuilder = new DbContextOptionsBuilder<DesignAidDbContext>();
            optionsBuilder.UseSqlite($"Data Source={dbPath}");
            using var context = new DesignAidDbContext(optionsBuilder.Options);

            var settingsService = new SettingsService();
            await settingsService.LoadAsync(context);
            await settingsService.SetAsync(context, key, value);

            Console.WriteLine($"設定を更新しました: {key} = {value}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"エラー: {ex.Message}");
            return 1;
        }
    }
}

/// <summary>
/// da config path - 各パスを表示。
/// </summary>
public class ConfigPathCommand : Command
{
    public ConfigPathCommand() : base("path", "データディレクトリや DB のパスを表示")
    {
        Handler = CommandHandler.Create(Execute);
    }

    private int Execute()
    {
        var dataDir = CommandHelper.EnsureDataDirectory();
        if (dataDir == null) return 3;
        var dbPath = CommandHelper.GetDatabasePath();

        Console.WriteLine("パス情報:");
        Console.WriteLine($"  プロジェクトディレクトリ: {dataDir}");
        Console.WriteLine($"  データベース: {dbPath}");
        Console.WriteLine($"  装置: {Path.Combine(dataDir, "assets")}");
        Console.WriteLine($"  コンポーネント: {Path.Combine(dataDir, "components")}");

        return 0;
    }
}
