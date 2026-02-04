using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace DesignAid.Commands;

/// <summary>
/// da backup - データディレクトリをバックアップ（ZIP/S3）。
/// Qdrant スナップショットと SQLite バックアップを含む。
/// </summary>
public class BackupCommand : Command
{
    public BackupCommand() : base("backup", "データディレクトリをバックアップする（Qdrant/DB dump含む）")
    {
        var dataPathOption = new Option<string?>(
            aliases: ["--data-path", "-d"],
            description: "バックアップするデータディレクトリのパス");

        var bucketOption = new Option<string?>(
            aliases: ["--bucket", "-b"],
            description: "S3 バケット名（省略時: config.json の設定を使用）");

        var prefixOption = new Option<string?>(
            aliases: ["--prefix"],
            description: "S3 プレフィックス（省略時: config.json の設定を使用）");

        var profileOption = new Option<string?>(
            aliases: ["--profile", "-p"],
            description: "AWS CLI プロファイル名（省略時: config.json の設定を使用）");

        var outputOption = new Option<string?>(
            aliases: ["--output", "-o"],
            description: "出力先 ZIP ファイルパス（--local-only 時に使用）");

        var dryRunOption = new Option<bool>(
            aliases: ["--dry-run"],
            description: "実際にはアップロードせず、実行内容を表示のみ");

        var localOnlyOption = new Option<bool>(
            aliases: ["--local-only", "-l"],
            description: "ローカルに ZIP を作成するのみ（S3 にアップロードしない）");

        var skipQdrantOption = new Option<bool>(
            aliases: ["--skip-qdrant"],
            description: "Qdrant スナップショットをスキップ");

        var skipDbOption = new Option<bool>(
            aliases: ["--skip-db"],
            description: "SQLite バックアップをスキップ（ファイルコピーのみ）");

        AddOption(dataPathOption);
        AddOption(bucketOption);
        AddOption(prefixOption);
        AddOption(profileOption);
        AddOption(outputOption);
        AddOption(dryRunOption);
        AddOption(localOnlyOption);
        AddOption(skipQdrantOption);
        AddOption(skipDbOption);

        Handler = CommandHandler.Create<string?, string?, string?, string?, string?, bool, bool, bool, bool>(ExecuteAsync);
    }

    private async Task<int> ExecuteAsync(
        string? dataPath,
        string? bucket,
        string? prefix,
        string? profile,
        string? output,
        bool dryRun,
        bool localOnly,
        bool skipQdrant,
        bool skipDb)
    {
        try
        {
            var dataDir = GetDataDirectory(dataPath);

            if (!Directory.Exists(dataDir))
            {
                Console.Error.WriteLine($"データディレクトリが見つかりません: {dataDir}");
                return 1;
            }

            // 設定ファイルから設定を読み込み
            var configPath = Path.Combine(dataDir, "config.json");
            LocalBackupConfig backupConfig = new();
            LocalQdrantConfig qdrantConfig = new();

            if (File.Exists(configPath))
            {
                var configJson = await File.ReadAllTextAsync(configPath);
                var options = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
                };
                var config = JsonSerializer.Deserialize<LocalConfig>(configJson, options);
                backupConfig = config?.Backup ?? new LocalBackupConfig();
                qdrantConfig = config?.Qdrant ?? new LocalQdrantConfig();
            }

            // オプションで上書き
            var s3Bucket = bucket ?? backupConfig.S3Bucket;
            var s3Prefix = prefix ?? backupConfig.S3Prefix;
            var awsProfile = profile ?? backupConfig.AwsProfile;

            // 一時ディレクトリを作成
            var tempDir = Path.Combine(Path.GetTempPath(), $"design-aid-backup-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);

            try
            {
                Console.WriteLine("バックアップを作成しています...");
                Console.WriteLine($"  ソース: {dataDir}");
                Console.WriteLine();

                // 1. SQLite バックアップ
                var dbPath = Path.Combine(dataDir, "design_aid.db");
                if (File.Exists(dbPath) && !skipDb)
                {
                    Console.WriteLine("[1/3] SQLite データベースをバックアップ中...");
                    if (!dryRun)
                    {
                        var dbBackupPath = Path.Combine(tempDir, "design_aid.db");
                        await CreateSqliteBackupAsync(dbPath, dbBackupPath);
                        Console.WriteLine($"      完了: {dbBackupPath}");
                    }
                    else
                    {
                        Console.WriteLine("      (ドライラン: スキップ)");
                    }
                }
                else if (skipDb)
                {
                    Console.WriteLine("[1/3] SQLite バックアップをスキップ（--skip-db）");
                }
                else
                {
                    Console.WriteLine("[1/3] SQLite データベースが見つかりません（スキップ）");
                }

                // 2. Qdrant スナップショット
                if (!skipQdrant && qdrantConfig.Enabled)
                {
                    Console.WriteLine("[2/3] Qdrant スナップショットを作成中...");
                    if (!dryRun)
                    {
                        var qdrantSnapshotPath = Path.Combine(tempDir, "qdrant_snapshot");
                        Directory.CreateDirectory(qdrantSnapshotPath);
                        var snapshotResult = await CreateQdrantSnapshotAsync(
                            qdrantConfig.Host,
                            qdrantConfig.GrpcPort - 1, // HTTP ポートは gRPC - 1
                            qdrantConfig.CollectionName,
                            qdrantSnapshotPath);

                        if (snapshotResult)
                        {
                            Console.WriteLine($"      完了: {qdrantSnapshotPath}");
                        }
                        else
                        {
                            Console.WriteLine("      警告: Qdrant スナップショットの作成に失敗（続行）");
                        }
                    }
                    else
                    {
                        Console.WriteLine("      (ドライラン: スキップ)");
                    }
                }
                else if (skipQdrant)
                {
                    Console.WriteLine("[2/3] Qdrant スナップショットをスキップ（--skip-qdrant）");
                }
                else
                {
                    Console.WriteLine("[2/3] Qdrant が無効（スキップ）");
                }

                // 3. データファイルをコピー
                Console.WriteLine("[3/3] データファイルをコピー中...");
                if (!dryRun)
                {
                    CopyDataFiles(dataDir, tempDir, skipDb);
                    Console.WriteLine("      完了");
                }
                else
                {
                    Console.WriteLine("      (ドライラン: スキップ)");
                }

                // ZIP ファイル名を生成
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var zipFileName = $"design-aid-backup_{timestamp}.zip";
                var zipFilePath = output ?? Path.Combine(Path.GetTempPath(), zipFileName);

                Console.WriteLine();
                Console.WriteLine($"ZIP ファイルを作成中: {zipFilePath}");

                if (!dryRun)
                {
                    // ZIP ファイルを作成
                    await CreateZipAsync(tempDir, zipFilePath);

                    var fileInfo = new FileInfo(zipFilePath);
                    Console.WriteLine($"  サイズ: {FormatFileSize(fileInfo.Length)}");
                }
                else
                {
                    Console.WriteLine("  (ドライラン: ZIP は作成されません)");
                }

                if (localOnly)
                {
                    if (!dryRun && output == null)
                    {
                        // 一時ディレクトリから現在のディレクトリに移動
                        var localZipPath = Path.Combine(Directory.GetCurrentDirectory(), zipFileName);
                        File.Move(zipFilePath, localZipPath, overwrite: true);
                        zipFilePath = localZipPath;
                    }
                    Console.WriteLine();
                    Console.WriteLine($"バックアップを作成しました: {zipFilePath}");
                    return 0;
                }

                // S3 へのアップロード
                if (string.IsNullOrEmpty(s3Bucket))
                {
                    Console.Error.WriteLine();
                    Console.Error.WriteLine("S3 バケットが指定されていません。");
                    Console.Error.WriteLine("config.json の backup.s3_bucket を設定するか、--bucket オプションを使用してください。");

                    // ローカルの ZIP は残す
                    if (!dryRun && output == null)
                    {
                        var localZipPath = Path.Combine(Directory.GetCurrentDirectory(), zipFileName);
                        File.Move(zipFilePath, localZipPath, overwrite: true);
                        Console.WriteLine();
                        Console.WriteLine($"ローカルバックアップ: {localZipPath}");
                    }
                    return 1;
                }

                var s3Key = s3Prefix.TrimEnd('/') + "/" + zipFileName;
                var s3Uri = $"s3://{s3Bucket}/{s3Key}";

                Console.WriteLine();
                Console.WriteLine("S3 にアップロードしています...");
                Console.WriteLine($"  バケット: {s3Bucket}");
                Console.WriteLine($"  キー: {s3Key}");
                Console.WriteLine($"  プロファイル: {awsProfile}");

                if (dryRun)
                {
                    Console.WriteLine();
                    Console.WriteLine("(ドライラン: 以下のコマンドが実行されます)");
                    Console.WriteLine($"  aws s3 cp \"{zipFilePath}\" \"{s3Uri}\" --profile {awsProfile}");
                    return 0;
                }

                // AWS CLI を使用してアップロード
                var result = await UploadToS3Async(zipFilePath, s3Uri, awsProfile);

                // 一時ファイルを削除
                if (File.Exists(zipFilePath) && output == null)
                {
                    File.Delete(zipFilePath);
                }

                if (result == 0)
                {
                    Console.WriteLine();
                    Console.WriteLine($"バックアップが完了しました: {s3Uri}");
                }

                return result;
            }
            finally
            {
                // 一時ディレクトリを削除
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"エラー: {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// SQLite データベースの整合性を保ったバックアップを作成する。
    /// </summary>
    private static async Task CreateSqliteBackupAsync(string sourcePath, string destPath)
    {
        // VACUUM INTO を使用してクリーンなバックアップを作成
        // これはロックなしでバックアップを作成できる
        var connectionString = $"Data Source={sourcePath}";

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        // VACUUM INTO でバックアップ作成
        var vacuumCommand = connection.CreateCommand();
        vacuumCommand.CommandText = $"VACUUM INTO '{destPath.Replace("'", "''")}'";
        await vacuumCommand.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Qdrant のスナップショットを作成してダウンロードする。
    /// </summary>
    private static async Task<bool> CreateQdrantSnapshotAsync(
        string host,
        int httpPort,
        string collectionName,
        string outputDir)
    {
        try
        {
            using var httpClient = new HttpClient
            {
                BaseAddress = new Uri($"http://{host}:{httpPort}"),
                Timeout = TimeSpan.FromMinutes(5)
            };

            // スナップショットを作成
            var createResponse = await httpClient.PostAsync(
                $"/collections/{collectionName}/snapshots",
                null);

            if (!createResponse.IsSuccessStatusCode)
            {
                Console.WriteLine($"      Qdrant エラー: {createResponse.StatusCode}");
                return false;
            }

            var createResult = await createResponse.Content.ReadFromJsonAsync<JsonDocument>();
            var snapshotName = createResult?.RootElement
                .GetProperty("result")
                .GetProperty("name")
                .GetString();

            if (string.IsNullOrEmpty(snapshotName))
            {
                Console.WriteLine("      スナップショット名を取得できませんでした");
                return false;
            }

            // スナップショットをダウンロード
            var downloadResponse = await httpClient.GetAsync(
                $"/collections/{collectionName}/snapshots/{snapshotName}");

            if (!downloadResponse.IsSuccessStatusCode)
            {
                Console.WriteLine($"      ダウンロードエラー: {downloadResponse.StatusCode}");
                return false;
            }

            var snapshotPath = Path.Combine(outputDir, $"{collectionName}_{snapshotName}");
            await using var fileStream = File.Create(snapshotPath);
            await downloadResponse.Content.CopyToAsync(fileStream);

            // メタデータファイルを作成（復元時に使用）
            var metadataPath = Path.Combine(outputDir, "metadata.json");
            var metadata = new
            {
                collectionName,
                snapshotName,
                createdAt = DateTime.UtcNow.ToString("o")
            };
            await File.WriteAllTextAsync(metadataPath,
                JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));

            return true;
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"      Qdrant 接続エラー: {ex.Message}");
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"      エラー: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// データファイルを一時ディレクトリにコピーする。
    /// </summary>
    private static void CopyDataFiles(string sourceDir, string destDir, bool skipDb)
    {
        var files = Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories);
        foreach (var file in files)
        {
            var relativePath = Path.GetRelativePath(sourceDir, file);
            var destPath = Path.Combine(destDir, relativePath);

            // SQLite 関連ファイルはスキップ（既にバックアップ済み or スキップ指定）
            var fileName = Path.GetFileName(file);
            if (fileName.StartsWith("design_aid.db"))
            {
                if (!skipDb)
                    continue; // バックアップ済み
                // skipDb の場合はそのままコピー
            }

            var destDirPath = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(destDirPath) && !Directory.Exists(destDirPath))
            {
                Directory.CreateDirectory(destDirPath);
            }

            File.Copy(file, destPath, overwrite: true);
        }
    }

    private static async Task CreateZipAsync(string sourceDir, string zipPath)
    {
        // 既存のファイルがあれば削除
        if (File.Exists(zipPath))
        {
            File.Delete(zipPath);
        }

        await Task.Run(() =>
        {
            ZipFile.CreateFromDirectory(sourceDir, zipPath, CompressionLevel.Optimal, false);
        });
    }

    private static async Task<int> UploadToS3Async(string localPath, string s3Uri, string profile)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "aws",
            Arguments = $"s3 cp \"{localPath}\" \"{s3Uri}\" --profile {profile}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            using var process = Process.Start(psi);
            if (process == null)
            {
                Console.Error.WriteLine("AWS CLI の起動に失敗しました。");
                Console.Error.WriteLine("AWS CLI がインストールされていることを確認してください。");
                return 1;
            }

            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync();

            if (!string.IsNullOrEmpty(output))
                Console.WriteLine(output);

            if (!string.IsNullOrEmpty(error))
                Console.Error.WriteLine(error);

            return process.ExitCode;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"AWS CLI の実行に失敗しました: {ex.Message}");
            Console.Error.WriteLine("AWS CLI がインストールされ、PATH に追加されていることを確認してください。");
            return 1;
        }
    }

    private static string GetDataDirectory(string? dataPath)
    {
        if (!string.IsNullOrEmpty(dataPath))
            return Path.GetFullPath(dataPath);

        return Path.Combine(Directory.GetCurrentDirectory(), "data");
    }

    private static string FormatFileSize(long bytes)
    {
        string[] sizes = ["B", "KB", "MB", "GB"];
        int order = 0;
        double size = bytes;

        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }

        return $"{size:0.##} {sizes[order]}";
    }
}
