using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
using System.Text.Json;
using System.Text.Json.Serialization;
using DesignAid.Configuration;

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
                Path.Combine(dataDir, "components"),
                Path.Combine(dataDir, "projects")
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

            // config.json の作成
            var configPath = Path.Combine(dataDir, "config.json");
            if (!File.Exists(configPath) || force)
            {
                var config = new LocalConfig
                {
                    Database = new LocalDatabaseConfig { Path = "design_aid.db" },
                    Qdrant = new LocalQdrantConfig
                    {
                        Host = "localhost",
                        GrpcPort = 6334,
                        Enabled = true,
                        CollectionName = "design_knowledge"
                    },
                    Embedding = new LocalEmbeddingConfig
                    {
                        Provider = "Mock",
                        Dimensions = 384
                    },
                    Backup = new LocalBackupConfig
                    {
                        S3Bucket = "",
                        S3Prefix = "design-aid-backup/",
                        AwsProfile = "default"
                    }
                };

                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                };

                var json = JsonSerializer.Serialize(config, options);
                await File.WriteAllTextAsync(configPath, json);

                Console.WriteLine($"  [作成] config.json");
            }
            else
            {
                Console.WriteLine($"  [存在] config.json（--force で上書き可能）");
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

            Console.WriteLine();
            Console.WriteLine("初期化が完了しました。");
            Console.WriteLine();
            Console.WriteLine("次のステップ:");
            Console.WriteLine($"  1. config.json を編集して設定を調整");
            Console.WriteLine($"  2. Qdrant を起動: docker compose up -d");
            Console.WriteLine($"  3. プロジェクトを登録: da project add <path>");

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
/// ローカル設定ファイル (config.json) の構造。
/// </summary>
public class LocalConfig
{
    [JsonPropertyName("database")]
    public LocalDatabaseConfig Database { get; set; } = new();

    [JsonPropertyName("qdrant")]
    public LocalQdrantConfig Qdrant { get; set; } = new();

    [JsonPropertyName("embedding")]
    public LocalEmbeddingConfig Embedding { get; set; } = new();

    [JsonPropertyName("backup")]
    public LocalBackupConfig Backup { get; set; } = new();
}

public class LocalDatabaseConfig
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = "design_aid.db";
}

public class LocalQdrantConfig
{
    [JsonPropertyName("host")]
    public string Host { get; set; } = "localhost";

    [JsonPropertyName("grpc_port")]
    public int GrpcPort { get; set; } = 6334;

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("collection_name")]
    public string CollectionName { get; set; } = "design_knowledge";
}

public class LocalEmbeddingConfig
{
    [JsonPropertyName("provider")]
    public string Provider { get; set; } = "Mock";

    [JsonPropertyName("dimensions")]
    public int Dimensions { get; set; } = 384;

    [JsonPropertyName("api_key")]
    public string? ApiKey { get; set; }

    [JsonPropertyName("endpoint")]
    public string? Endpoint { get; set; }

    [JsonPropertyName("model")]
    public string? Model { get; set; }
}

public class LocalBackupConfig
{
    [JsonPropertyName("s3_bucket")]
    public string S3Bucket { get; set; } = "";

    [JsonPropertyName("s3_prefix")]
    public string S3Prefix { get; set; } = "design-aid-backup/";

    [JsonPropertyName("aws_profile")]
    public string AwsProfile { get; set; } = "default";
}
