using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;

namespace DesignAid.Commands;

/// <summary>
/// da backup - データディレクトリを AWS S3 にバックアップ。
/// </summary>
public class BackupCommand : Command
{
    public BackupCommand() : base("backup", "データディレクトリを AWS S3 にバックアップする")
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

        var dryRunOption = new Option<bool>(
            aliases: ["--dry-run"],
            description: "実際にはアップロードせず、実行内容を表示のみ");

        var localOnlyOption = new Option<bool>(
            aliases: ["--local-only", "-l"],
            description: "ローカルに ZIP を作成するのみ（S3 にアップロードしない）");

        AddOption(dataPathOption);
        AddOption(bucketOption);
        AddOption(prefixOption);
        AddOption(profileOption);
        AddOption(dryRunOption);
        AddOption(localOnlyOption);

        Handler = CommandHandler.Create<string?, string?, string?, string?, bool, bool>(ExecuteAsync);
    }

    private async Task<int> ExecuteAsync(
        string? dataPath,
        string? bucket,
        string? prefix,
        string? profile,
        bool dryRun,
        bool localOnly)
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

            if (File.Exists(configPath))
            {
                var configJson = await File.ReadAllTextAsync(configPath);
                var options = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
                };
                var config = JsonSerializer.Deserialize<LocalConfig>(configJson, options);
                backupConfig = config?.Backup ?? new LocalBackupConfig();
            }

            // オプションで上書き
            var s3Bucket = bucket ?? backupConfig.S3Bucket;
            var s3Prefix = prefix ?? backupConfig.S3Prefix;
            var awsProfile = profile ?? backupConfig.AwsProfile;

            // ZIP ファイル名を生成
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var zipFileName = $"design-aid-backup_{timestamp}.zip";
            var zipFilePath = Path.Combine(Path.GetTempPath(), zipFileName);

            Console.WriteLine("バックアップを作成しています...");
            Console.WriteLine($"  ソース: {dataDir}");
            Console.WriteLine($"  ZIP: {zipFilePath}");

            if (!dryRun)
            {
                // ZIP ファイルを作成
                await CreateZipAsync(dataDir, zipFilePath);

                var fileInfo = new FileInfo(zipFilePath);
                Console.WriteLine($"  サイズ: {FormatFileSize(fileInfo.Length)}");
            }
            else
            {
                Console.WriteLine("  (ドライラン: ZIP は作成されません)");
            }

            if (localOnly)
            {
                if (!dryRun)
                {
                    // 一時ディレクトリから現在のディレクトリに移動
                    var localZipPath = Path.Combine(Directory.GetCurrentDirectory(), zipFileName);
                    File.Move(zipFilePath, localZipPath, overwrite: true);
                    Console.WriteLine();
                    Console.WriteLine($"バックアップを作成しました: {localZipPath}");
                }
                return 0;
            }

            // S3 へのアップロード
            if (string.IsNullOrEmpty(s3Bucket))
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine("S3 バケットが指定されていません。");
                Console.Error.WriteLine("config.json の backup.s3_bucket を設定するか、--bucket オプションを使用してください。");

                // ローカルの ZIP は残す
                if (!dryRun)
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
            if (File.Exists(zipFilePath))
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
        catch (Exception ex)
        {
            Console.Error.WriteLine($"エラー: {ex.Message}");
            return 1;
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
            using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);

            var files = Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                // .db-shm, .db-wal は除外（SQLite の一時ファイル）
                var ext = Path.GetExtension(file).ToLower();
                if (ext == ".db-shm" || ext == ".db-wal")
                    continue;

                var relativePath = Path.GetRelativePath(sourceDir, file);
                archive.CreateEntryFromFile(file, relativePath, CompressionLevel.Optimal);
            }
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
