using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text.Json;

namespace DesignAid.Commands;

/// <summary>
/// da restore - バックアップからデータを復元する。
/// ZIP ファイルを展開し、SQLite DB と Qdrant スナップショットを復元する。
/// </summary>
public class RestoreCommand : Command
{
    public RestoreCommand() : base("restore", "バックアップからデータを復元する")
    {
        var sourceArgument = new Argument<string>(
            name: "source",
            description: "復元元の ZIP ファイルパス、または S3 URI (s3://bucket/key)");

        var dataPathOption = new Option<string?>(
            aliases: ["--data-path", "-d"],
            description: "復元先のデータディレクトリのパス");

        var profileOption = new Option<string?>(
            aliases: ["--profile", "-p"],
            description: "AWS CLI プロファイル名（S3 からダウンロード時）");

        var skipQdrantOption = new Option<bool>(
            aliases: ["--skip-qdrant"],
            description: "Qdrant スナップショットの復元をスキップ");

        var forceOption = new Option<bool>(
            aliases: ["--force", "-f"],
            description: "既存データを上書きする（確認なし）");

        var dryRunOption = new Option<bool>(
            aliases: ["--dry-run"],
            description: "実際には復元せず、内容を表示のみ");

        AddArgument(sourceArgument);
        AddOption(dataPathOption);
        AddOption(profileOption);
        AddOption(skipQdrantOption);
        AddOption(forceOption);
        AddOption(dryRunOption);

        Handler = CommandHandler.Create<string, string?, string?, bool, bool, bool>(ExecuteAsync);
    }

    private async Task<int> ExecuteAsync(
        string source,
        string? dataPath,
        string? profile,
        bool skipQdrant,
        bool force,
        bool dryRun)
    {
        try
        {
            var dataDir = GetDataDirectory(dataPath);
            string zipFilePath;

            // S3 からダウンロードが必要かどうか
            if (source.StartsWith("s3://", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("S3 からバックアップをダウンロードしています...");
                Console.WriteLine($"  URI: {source}");

                if (dryRun)
                {
                    Console.WriteLine("  (ドライラン: ダウンロードはスキップ)");
                    return 0;
                }

                var awsProfile = profile ?? "default";
                zipFilePath = Path.Combine(Path.GetTempPath(), $"design-aid-restore-{Guid.NewGuid():N}.zip");
                var downloadResult = await DownloadFromS3Async(source, zipFilePath, awsProfile);

                if (downloadResult != 0)
                {
                    Console.Error.WriteLine("S3 からのダウンロードに失敗しました。");
                    return downloadResult;
                }

                Console.WriteLine("  ダウンロード完了");
            }
            else
            {
                zipFilePath = Path.GetFullPath(source);
                if (!File.Exists(zipFilePath))
                {
                    Console.Error.WriteLine($"バックアップファイルが見つかりません: {zipFilePath}");
                    return 1;
                }
            }

            try
            {
                // ZIP ファイルの内容を確認
                Console.WriteLine();
                Console.WriteLine("バックアップの内容を確認しています...");

                using var archive = ZipFile.OpenRead(zipFilePath);

                var hasDb = archive.Entries.Any(e =>
                    e.FullName.Equals("design_aid.db", StringComparison.OrdinalIgnoreCase));
                var hasQdrantSnapshot = archive.Entries.Any(e =>
                    e.FullName.StartsWith("qdrant_snapshot/", StringComparison.OrdinalIgnoreCase));
                var hasConfig = archive.Entries.Any(e =>
                    e.FullName.Equals("config.json", StringComparison.OrdinalIgnoreCase));
                var hasAssets = archive.Entries.Any(e =>
                    e.FullName.StartsWith("assets/", StringComparison.OrdinalIgnoreCase));
                var hasComponents = archive.Entries.Any(e =>
                    e.FullName.StartsWith("components/", StringComparison.OrdinalIgnoreCase));

                Console.WriteLine($"  SQLite DB: {(hasDb ? "あり" : "なし")}");
                Console.WriteLine($"  Qdrant スナップショット: {(hasQdrantSnapshot ? "あり" : "なし")}");
                Console.WriteLine($"  config.json: {(hasConfig ? "あり" : "なし")}");
                Console.WriteLine($"  assets/: {(hasAssets ? "あり" : "なし")}");
                Console.WriteLine($"  components/: {(hasComponents ? "あり" : "なし")}");

                // 既存データのチェック
                if (Directory.Exists(dataDir) && Directory.GetFileSystemEntries(dataDir).Length > 0)
                {
                    if (!force && !dryRun)
                    {
                        Console.WriteLine();
                        Console.WriteLine($"警告: 復元先ディレクトリにデータが存在します: {dataDir}");
                        Console.Write("既存データを上書きしますか？ (y/N): ");
                        var response = Console.ReadLine();

                        if (!response?.Trim().Equals("y", StringComparison.OrdinalIgnoreCase) == true)
                        {
                            Console.WriteLine("復元をキャンセルしました。");
                            return 1;
                        }
                    }
                    else if (!force)
                    {
                        Console.WriteLine();
                        Console.WriteLine($"復元先: {dataDir}（既存データあり）");
                    }
                }

                Console.WriteLine();
                Console.WriteLine($"復元先: {dataDir}");

                if (dryRun)
                {
                    Console.WriteLine();
                    Console.WriteLine("(ドライラン: 実際の復元はスキップ)");
                    return 0;
                }

                // 復元を実行
                Console.WriteLine();
                Console.WriteLine("復元を開始します...");

                // 1. データファイルを展開
                Console.WriteLine("[1/3] データファイルを展開中...");
                await ExtractDataFilesAsync(archive, dataDir, skipQdrant);
                Console.WriteLine("      完了");

                // 2. Qdrant スナップショットを復元
                if (hasQdrantSnapshot && !skipQdrant)
                {
                    Console.WriteLine("[2/3] Qdrant スナップショットを復元中...");

                    // config.json から Qdrant 設定を読み込み
                    var configPath = Path.Combine(dataDir, "config.json");
                    LocalQdrantConfig qdrantConfig = new();

                    if (File.Exists(configPath))
                    {
                        var configJson = await File.ReadAllTextAsync(configPath);
                        var options = new JsonSerializerOptions
                        {
                            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
                        };
                        var config = JsonSerializer.Deserialize<LocalConfig>(configJson, options);
                        qdrantConfig = config?.Qdrant ?? new LocalQdrantConfig();
                    }

                    if (qdrantConfig.Enabled)
                    {
                        var snapshotDir = Path.Combine(dataDir, "qdrant_snapshot");
                        var restoreResult = await RestoreQdrantSnapshotAsync(
                            qdrantConfig.Host,
                            qdrantConfig.GrpcPort - 1, // HTTP ポート
                            snapshotDir);

                        if (restoreResult)
                        {
                            Console.WriteLine("      完了");
                        }
                        else
                        {
                            Console.WriteLine("      警告: Qdrant スナップショットの復元に失敗");
                        }

                        // スナップショットディレクトリを削除
                        if (Directory.Exists(snapshotDir))
                        {
                            Directory.Delete(snapshotDir, true);
                        }
                    }
                    else
                    {
                        Console.WriteLine("      スキップ（Qdrant が無効）");
                    }
                }
                else if (skipQdrant)
                {
                    Console.WriteLine("[2/3] Qdrant スナップショットをスキップ（--skip-qdrant）");
                }
                else
                {
                    Console.WriteLine("[2/3] Qdrant スナップショットなし（スキップ）");
                }

                Console.WriteLine("[3/3] 完了");

                Console.WriteLine();
                Console.WriteLine("復元が完了しました。");
                Console.WriteLine();
                Console.WriteLine("次のステップ:");
                Console.WriteLine("  1. da status で状態を確認");
                Console.WriteLine("  2. da check で整合性を確認");

                return 0;
            }
            finally
            {
                // S3 からダウンロードした場合、一時ファイルを削除
                if (source.StartsWith("s3://", StringComparison.OrdinalIgnoreCase) && File.Exists(zipFilePath))
                {
                    File.Delete(zipFilePath);
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
    /// データファイルを展開する。
    /// </summary>
    private static async Task ExtractDataFilesAsync(ZipArchive archive, string destDir, bool skipQdrant)
    {
        Directory.CreateDirectory(destDir);

        foreach (var entry in archive.Entries)
        {
            // Qdrant スナップショットは後で処理
            if (skipQdrant && entry.FullName.StartsWith("qdrant_snapshot/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // 空のディレクトリエントリはスキップ
            if (string.IsNullOrEmpty(entry.Name))
            {
                continue;
            }

            var destPath = Path.Combine(destDir, entry.FullName);
            var destDirPath = Path.GetDirectoryName(destPath);

            if (!string.IsNullOrEmpty(destDirPath) && !Directory.Exists(destDirPath))
            {
                Directory.CreateDirectory(destDirPath);
            }

            await Task.Run(() => entry.ExtractToFile(destPath, overwrite: true));
        }
    }

    /// <summary>
    /// Qdrant スナップショットを復元する。
    /// </summary>
    private static async Task<bool> RestoreQdrantSnapshotAsync(
        string host,
        int httpPort,
        string snapshotDir)
    {
        try
        {
            // メタデータを読み込み
            var metadataPath = Path.Combine(snapshotDir, "metadata.json");
            if (!File.Exists(metadataPath))
            {
                Console.WriteLine("      メタデータファイルが見つかりません");
                return false;
            }

            var metadataJson = await File.ReadAllTextAsync(metadataPath);
            using var metadata = JsonDocument.Parse(metadataJson);
            var collectionName = metadata.RootElement.GetProperty("collectionName").GetString();
            var snapshotName = metadata.RootElement.GetProperty("snapshotName").GetString();

            if (string.IsNullOrEmpty(collectionName) || string.IsNullOrEmpty(snapshotName))
            {
                Console.WriteLine("      メタデータが不完全です");
                return false;
            }

            // スナップショットファイルを検索
            var snapshotFiles = Directory.GetFiles(snapshotDir)
                .Where(f => !f.EndsWith("metadata.json", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (snapshotFiles.Count == 0)
            {
                Console.WriteLine("      スナップショットファイルが見つかりません");
                return false;
            }

            var snapshotFilePath = snapshotFiles[0];

            using var httpClient = new HttpClient
            {
                BaseAddress = new Uri($"http://{host}:{httpPort}"),
                Timeout = TimeSpan.FromMinutes(10)
            };

            // 既存のコレクションを削除（存在する場合）
            try
            {
                await httpClient.DeleteAsync($"/collections/{collectionName}");
            }
            catch
            {
                // コレクションが存在しない場合は無視
            }

            // スナップショットをアップロードして復元
            await using var fileStream = File.OpenRead(snapshotFilePath);
            using var content = new StreamContent(fileStream);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

            // Qdrant REST API でスナップショットを復元
            // POST /collections/{collection_name}/snapshots/upload
            var response = await httpClient.PutAsync(
                $"/collections/{collectionName}/snapshots/upload?priority=snapshot",
                content);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"      復元エラー: {response.StatusCode}");
                Console.WriteLine($"      {errorContent}");
                return false;
            }

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
    /// S3 からファイルをダウンロードする。
    /// </summary>
    private static async Task<int> DownloadFromS3Async(string s3Uri, string localPath, string profile)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "aws",
            Arguments = $"s3 cp \"{s3Uri}\" \"{localPath}\" --profile {profile}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            using var process = System.Diagnostics.Process.Start(psi);
            if (process == null)
            {
                Console.Error.WriteLine("AWS CLI の起動に失敗しました。");
                return 1;
            }

            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (!string.IsNullOrEmpty(error) && process.ExitCode != 0)
            {
                Console.Error.WriteLine(error);
            }

            return process.ExitCode;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"AWS CLI の実行に失敗しました: {ex.Message}");
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
