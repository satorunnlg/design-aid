using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
using System.Text.Json;

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
        var dataPathOption = new Option<string?>(
            aliases: ["--data-path", "-d"],
            description: "データディレクトリのパス（省略時: カレントディレクトリの data/）");

        var jsonOption = new Option<bool>(
            aliases: ["--json"],
            description: "JSON 形式で出力");

        AddOption(dataPathOption);
        AddOption(jsonOption);

        Handler = CommandHandler.Create<string?, bool>(ExecuteAsync);
    }

    private async Task<int> ExecuteAsync(string? dataPath, bool json)
    {
        try
        {
            var dataDir = GetDataDirectory(dataPath);
            var configPath = Path.Combine(dataDir, "config.json");

            if (!File.Exists(configPath))
            {
                Console.Error.WriteLine($"設定ファイルが見つかりません: {configPath}");
                Console.Error.WriteLine("da setup を実行してデータディレクトリを初期化してください。");
                return 1;
            }

            var configJson = await File.ReadAllTextAsync(configPath);

            if (json)
            {
                Console.WriteLine(configJson);
            }
            else
            {
                var config = JsonSerializer.Deserialize<LocalConfig>(configJson, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
                });

                Console.WriteLine("Design Aid 設定");
                Console.WriteLine($"  設定ファイル: {configPath}");
                Console.WriteLine();
                Console.WriteLine("データベース:");
                Console.WriteLine($"  パス: {config?.Database.Path}");
                Console.WriteLine();
                Console.WriteLine("Qdrant:");
                Console.WriteLine($"  ホスト: {config?.Qdrant.Host}");
                Console.WriteLine($"  gRPC ポート: {config?.Qdrant.GrpcPort}");
                Console.WriteLine($"  有効: {config?.Qdrant.Enabled}");
                Console.WriteLine($"  コレクション: {config?.Qdrant.CollectionName}");
                Console.WriteLine();
                Console.WriteLine("埋め込み:");
                Console.WriteLine($"  プロバイダー: {config?.Embedding.Provider}");
                Console.WriteLine($"  次元数: {config?.Embedding.Dimensions}");
                if (!string.IsNullOrEmpty(config?.Embedding.Model))
                    Console.WriteLine($"  モデル: {config?.Embedding.Model}");
                Console.WriteLine();
                Console.WriteLine("バックアップ:");
                Console.WriteLine($"  S3 バケット: {(string.IsNullOrEmpty(config?.Backup.S3Bucket) ? "(未設定)" : config.Backup.S3Bucket)}");
                Console.WriteLine($"  S3 プレフィックス: {config?.Backup.S3Prefix}");
                Console.WriteLine($"  AWS プロファイル: {config?.Backup.AwsProfile}");
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"エラー: {ex.Message}");
            return 1;
        }
    }

    private static string GetDataDirectory(string? dataPath)
    {
        if (!string.IsNullOrEmpty(dataPath))
            return Path.GetFullPath(dataPath);

        return Path.Combine(Directory.GetCurrentDirectory(), "data");
    }
}

/// <summary>
/// da config set - 設定値を変更。
/// </summary>
public class ConfigSetCommand : Command
{
    public ConfigSetCommand() : base("set", "設定値を変更する")
    {
        var keyArgument = new Argument<string>("key", "設定キー（例: qdrant.enabled, embedding.provider）");
        var valueArgument = new Argument<string>("value", "設定値");

        var dataPathOption = new Option<string?>(
            aliases: ["--data-path", "-d"],
            description: "データディレクトリのパス");

        AddArgument(keyArgument);
        AddArgument(valueArgument);
        AddOption(dataPathOption);

        Handler = CommandHandler.Create<string, string, string?>(ExecuteAsync);
    }

    private async Task<int> ExecuteAsync(string key, string value, string? dataPath)
    {
        try
        {
            var dataDir = GetDataDirectory(dataPath);
            var configPath = Path.Combine(dataDir, "config.json");

            if (!File.Exists(configPath))
            {
                Console.Error.WriteLine($"設定ファイルが見つかりません: {configPath}");
                return 1;
            }

            var configJson = await File.ReadAllTextAsync(configPath);
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
            };
            var config = JsonSerializer.Deserialize<LocalConfig>(configJson, options) ?? new LocalConfig();

            // キーに基づいて設定を更新
            var parts = key.ToLower().Split('.');
            var updated = false;

            if (parts.Length == 2)
            {
                updated = UpdateConfig(config, parts[0], parts[1], value);
            }

            if (!updated)
            {
                Console.Error.WriteLine($"不明な設定キー: {key}");
                Console.Error.WriteLine();
                Console.Error.WriteLine("利用可能なキー:");
                Console.Error.WriteLine("  database.path");
                Console.Error.WriteLine("  qdrant.host, qdrant.grpc_port, qdrant.enabled, qdrant.collection_name");
                Console.Error.WriteLine("  embedding.provider, embedding.dimensions, embedding.api_key, embedding.model");
                Console.Error.WriteLine("  backup.s3_bucket, backup.s3_prefix, backup.aws_profile");
                return 1;
            }

            // 保存
            var writeOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
            };
            var newJson = JsonSerializer.Serialize(config, writeOptions);
            await File.WriteAllTextAsync(configPath, newJson);

            Console.WriteLine($"設定を更新しました: {key} = {value}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"エラー: {ex.Message}");
            return 1;
        }
    }

    private static bool UpdateConfig(LocalConfig config, string section, string key, string value)
    {
        switch (section)
        {
            case "database":
                if (key == "path") { config.Database.Path = value; return true; }
                break;

            case "qdrant":
                switch (key)
                {
                    case "host": config.Qdrant.Host = value; return true;
                    case "grpc_port" when int.TryParse(value, out var port): config.Qdrant.GrpcPort = port; return true;
                    case "enabled": config.Qdrant.Enabled = value.ToLower() == "true"; return true;
                    case "collection_name": config.Qdrant.CollectionName = value; return true;
                }
                break;

            case "embedding":
                switch (key)
                {
                    case "provider": config.Embedding.Provider = value; return true;
                    case "dimensions" when int.TryParse(value, out var dim): config.Embedding.Dimensions = dim; return true;
                    case "api_key": config.Embedding.ApiKey = value; return true;
                    case "model": config.Embedding.Model = value; return true;
                    case "endpoint": config.Embedding.Endpoint = value; return true;
                }
                break;

            case "backup":
                switch (key)
                {
                    case "s3_bucket": config.Backup.S3Bucket = value; return true;
                    case "s3_prefix": config.Backup.S3Prefix = value; return true;
                    case "aws_profile": config.Backup.AwsProfile = value; return true;
                }
                break;
        }

        return false;
    }

    private static string GetDataDirectory(string? dataPath)
    {
        if (!string.IsNullOrEmpty(dataPath))
            return Path.GetFullPath(dataPath);

        return Path.Combine(Directory.GetCurrentDirectory(), "data");
    }
}

/// <summary>
/// da config path - 各パスを表示。
/// </summary>
public class ConfigPathCommand : Command
{
    public ConfigPathCommand() : base("path", "設定ファイルやデータディレクトリのパスを表示")
    {
        var dataPathOption = new Option<string?>(
            aliases: ["--data-path", "-d"],
            description: "データディレクトリのパス");

        AddOption(dataPathOption);

        Handler = CommandHandler.Create<string?>(Execute);
    }

    private int Execute(string? dataPath)
    {
        var dataDir = GetDataDirectory(dataPath);

        Console.WriteLine("パス情報:");
        Console.WriteLine($"  データディレクトリ: {dataDir}");
        Console.WriteLine($"  設定ファイル: {Path.Combine(dataDir, "config.json")}");
        Console.WriteLine($"  装置: {Path.Combine(dataDir, "assets")}");
        Console.WriteLine($"  コンポーネント: {Path.Combine(dataDir, "components")}");

        return 0;
    }

    private static string GetDataDirectory(string? dataPath)
    {
        if (!string.IsNullOrEmpty(dataPath))
            return Path.GetFullPath(dataPath);

        return Path.Combine(Directory.GetCurrentDirectory(), "data");
    }
}
