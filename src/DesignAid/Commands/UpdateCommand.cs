using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Json;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;

namespace DesignAid.Commands;

/// <summary>
/// ツールを最新版に更新するコマンド。
/// GitHub Release から最新バージョンをダウンロードして更新する。
/// </summary>
public class UpdateCommand : Command
{
    private const string GitHubRepo = "satorunnlg/design-aid";
    private const string GitHubApiBase = "https://api.github.com";

    public UpdateCommand() : base("update", "ツールを最新版に更新")
    {
        this.Add(new Option<bool>("--check", "更新があるか確認のみ（更新しない）"));
        this.Add(new Option<bool>("--force", "同じバージョンでも強制的に更新"));

        this.Handler = CommandHandler.Create<bool, bool>(ExecuteAsync);
    }

    private static async Task ExecuteAsync(bool check, bool force)
    {
        var currentVersion = GetCurrentVersion();
        Console.WriteLine($"現在のバージョン: {currentVersion}");

        try
        {
            var latestRelease = await GetLatestReleaseAsync();
            if (latestRelease == null)
            {
                Console.Error.WriteLine("[ERROR] リリース情報の取得に失敗しました");
                return;
            }

            var latestVersion = latestRelease.TagName?.TrimStart('v') ?? "unknown";
            Console.WriteLine($"最新のバージョン: {latestVersion}");

            // バージョン比較
            var needsUpdate = force || IsNewerVersion(currentVersion, latestVersion);

            if (!needsUpdate)
            {
                Console.WriteLine();
                Console.WriteLine("既に最新バージョンです。");
                return;
            }

            if (check)
            {
                Console.WriteLine();
                Console.WriteLine($"新しいバージョン {latestVersion} が利用可能です。");
                Console.WriteLine($"リリースページ: {latestRelease.HtmlUrl}");
                return;
            }

            // 適切なアセットを選択
            var assetName = GetAssetNameForCurrentPlatform();
            var asset = latestRelease.Assets?.FirstOrDefault(a => a.Name == assetName);

            if (asset == null)
            {
                Console.Error.WriteLine($"[ERROR] お使いのプラットフォーム用のアセットが見つかりません: {assetName}");
                Console.WriteLine($"手動でダウンロードしてください: {latestRelease.HtmlUrl}");
                return;
            }

            Console.WriteLine();
            Console.WriteLine($"ダウンロード中: {asset.Name} ({FormatSize(asset.Size)})");

            // 一時ディレクトリにダウンロード
            var tempDir = Path.Combine(Path.GetTempPath(), $"daid-update-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);

            try
            {
                var downloadPath = Path.Combine(tempDir, asset.Name);
                await DownloadAssetAsync(asset.BrowserDownloadUrl, downloadPath);

                Console.WriteLine("展開中...");
                var extractDir = Path.Combine(tempDir, "extracted");
                ExtractArchive(downloadPath, extractDir);

                // 更新を実行
                var currentExePath = Process.GetCurrentProcess().MainModule?.FileName
                    ?? throw new InvalidOperationException("実行ファイルのパスを取得できません");

                Console.WriteLine("更新を適用中...");
                await ApplyUpdateAsync(extractDir, currentExePath, tempDir);
            }
            catch
            {
                // エラー時はクリーンアップ
                try { Directory.Delete(tempDir, true); } catch { }
                throw;
            }
        }
        catch (HttpRequestException ex)
        {
            Console.Error.WriteLine($"[ERROR] ネットワークエラー: {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ERROR] 更新に失敗しました: {ex.Message}");
        }
    }

    private static string GetCurrentVersion()
    {
        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0";

        // +metadata を除去
        var plusIndex = version.IndexOf('+');
        if (plusIndex >= 0)
            version = version[..plusIndex];

        return version;
    }

    private static bool IsNewerVersion(string current, string latest)
    {
        // シンプルなバージョン比較（セマンティックバージョニング対応）
        try
        {
            // -alpha, -beta 等のサフィックスを処理
            static (Version ver, string suffix) ParseVersion(string v)
            {
                var dashIndex = v.IndexOf('-');
                var versionPart = dashIndex >= 0 ? v[..dashIndex] : v;
                var suffix = dashIndex >= 0 ? v[dashIndex..] : "";

                // 不完全なバージョン番号を補完
                var parts = versionPart.Split('.');
                while (parts.Length < 3)
                {
                    versionPart += ".0";
                    parts = versionPart.Split('.');
                }

                return (Version.Parse(versionPart), suffix);
            }

            var (currentVer, currentSuffix) = ParseVersion(current);
            var (latestVer, latestSuffix) = ParseVersion(latest);

            var comparison = latestVer.CompareTo(currentVer);
            if (comparison != 0)
                return comparison > 0;

            // バージョンが同じ場合、サフィックスで比較
            // 空 > rc > beta > alpha
            if (string.IsNullOrEmpty(latestSuffix) && !string.IsNullOrEmpty(currentSuffix))
                return true; // latest が安定版、current がプレリリース

            return false;
        }
        catch
        {
            // パースに失敗した場合は文字列比較
            return string.Compare(latest, current, StringComparison.OrdinalIgnoreCase) > 0;
        }
    }

    private static string GetAssetNameForCurrentPlatform()
    {
        var rid = GetRuntimeIdentifier();
        var extension = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "zip" : "tar.gz";
        return $"design-aid-{rid}.{extension}";
    }

    private static string GetRuntimeIdentifier()
    {
        var os = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "win"
               : RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "linux"
               : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "osx"
               : throw new PlatformNotSupportedException("サポートされていないOSです");

        var arch = RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            _ => throw new PlatformNotSupportedException($"サポートされていないアーキテクチャです: {RuntimeInformation.OSArchitecture}")
        };

        return $"{os}-{arch}";
    }

    private static async Task<GitHubRelease?> GetLatestReleaseAsync()
    {
        using var client = CreateHttpClient();
        var url = $"{GitHubApiBase}/repos/{GitHubRepo}/releases/latest";

        var response = await client.GetAsync(url);
        if (!response.IsSuccessStatusCode)
        {
            // latest が無い場合（プレリリースのみ）は releases から取得
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                url = $"{GitHubApiBase}/repos/{GitHubRepo}/releases";
                var releases = await client.GetFromJsonAsync<GitHubRelease[]>(url);
                return releases?.FirstOrDefault();
            }
            return null;
        }

        return await response.Content.ReadFromJsonAsync<GitHubRelease>();
    }

    private static async Task DownloadAssetAsync(string url, string destPath)
    {
        using var client = CreateHttpClient();
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1;
        await using var contentStream = await response.Content.ReadAsStreamAsync();
        await using var fileStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

        var buffer = new byte[8192];
        var downloadedBytes = 0L;
        int bytesRead;

        while ((bytesRead = await contentStream.ReadAsync(buffer)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
            downloadedBytes += bytesRead;

            if (totalBytes > 0)
            {
                var progress = (int)(downloadedBytes * 100 / totalBytes);
                Console.Write($"\r  {progress}% ({FormatSize(downloadedBytes)} / {FormatSize(totalBytes)})");
            }
        }
        Console.WriteLine();
    }

    private static void ExtractArchive(string archivePath, string destDir)
    {
        Directory.CreateDirectory(destDir);

        if (archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            ZipFile.ExtractToDirectory(archivePath, destDir);
        }
        else if (archivePath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
        {
            // tar.gz の展開
            ExtractTarGz(archivePath, destDir);
        }
        else
        {
            throw new NotSupportedException($"サポートされていないアーカイブ形式: {archivePath}");
        }
    }

    private static void ExtractTarGz(string archivePath, string destDir)
    {
        // .NET の GZipStream と tar 処理
        using var fileStream = File.OpenRead(archivePath);
        using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
        using var memoryStream = new MemoryStream();
        gzipStream.CopyTo(memoryStream);
        memoryStream.Position = 0;

        // シンプルな tar 展開（512バイトブロック）
        var buffer = new byte[512];
        while (memoryStream.Read(buffer, 0, 512) == 512)
        {
            // 空のブロック（終端）をチェック
            if (buffer.All(b => b == 0))
                break;

            // ファイル名（0-99）
            var nameBytes = buffer.TakeWhile(b => b != 0).ToArray();
            if (nameBytes.Length == 0)
                break;

            var name = System.Text.Encoding.UTF8.GetString(nameBytes);

            // ファイルサイズ（124-135、8進数）
            var sizeStr = System.Text.Encoding.ASCII.GetString(buffer, 124, 11).Trim('\0', ' ');
            var size = string.IsNullOrEmpty(sizeStr) ? 0 : Convert.ToInt64(sizeStr, 8);

            // タイプフラグ（156）
            var typeFlag = (char)buffer[156];

            var destPath = Path.Combine(destDir, name.TrimEnd('/'));

            if (typeFlag == '5' || name.EndsWith('/'))
            {
                // ディレクトリ
                Directory.CreateDirectory(destPath);
            }
            else if (typeFlag == '0' || typeFlag == '\0')
            {
                // 通常ファイル
                var dir = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                var fileData = new byte[size];
                memoryStream.Read(fileData, 0, (int)size);
                File.WriteAllBytes(destPath, fileData);

                // Unix の場合は実行権限を付与
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    // mode（100-107、8進数）
                    var modeStr = System.Text.Encoding.ASCII.GetString(buffer, 100, 7).Trim('\0', ' ');
                    if (!string.IsNullOrEmpty(modeStr))
                    {
                        var mode = Convert.ToInt32(modeStr, 8);
                        if ((mode & 0x49) != 0) // 実行ビット
                        {
                            try
                            {
                                var psi = new ProcessStartInfo("chmod", $"+x \"{destPath}\"")
                                {
                                    UseShellExecute = false,
                                    CreateNoWindow = true
                                };
                                Process.Start(psi)?.WaitForExit();
                            }
                            catch { }
                        }
                    }
                }

                // パディングをスキップ（512バイト境界）
                var padding = (512 - (int)(size % 512)) % 512;
                if (padding > 0)
                    memoryStream.Seek(padding, SeekOrigin.Current);
            }
            else
            {
                // その他のタイプはスキップ
                var blocks = (int)((size + 511) / 512);
                memoryStream.Seek(blocks * 512, SeekOrigin.Current);
            }
        }
    }

    private static async Task ApplyUpdateAsync(string extractDir, string currentExePath, string tempDir)
    {
        // 展開されたファイルから実行ファイルを探す
        var exeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "daid.exe" : "daid";
        var newExePath = Directory.GetFiles(extractDir, exeName, SearchOption.AllDirectories).FirstOrDefault()
            ?? Directory.GetFiles(extractDir, "DesignAid*", SearchOption.AllDirectories)
                .FirstOrDefault(f => !f.EndsWith(".pdb") && !f.EndsWith(".deps.json"));

        if (newExePath == null)
        {
            throw new FileNotFoundException("更新用の実行ファイルが見つかりません");
        }

        var currentDir = Path.GetDirectoryName(currentExePath)!;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Windows: バッチファイルで更新
            var batchPath = Path.Combine(tempDir, "update.bat");
            var batchContent = $"""
                @echo off
                echo 更新を適用しています...
                timeout /t 2 /nobreak >nul
                xcopy /Y /E "{Path.GetDirectoryName(newExePath)}\*" "{currentDir}\"
                echo 更新が完了しました。
                rd /s /q "{tempDir}"
                pause
                """;
            await File.WriteAllTextAsync(batchPath, batchContent, System.Text.Encoding.GetEncoding("shift_jis"));

            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{batchPath}\"",
                UseShellExecute = true,
                CreateNoWindow = false
            });
        }
        else
        {
            // Linux/macOS: シェルスクリプトで更新
            var scriptPath = Path.Combine(tempDir, "update.sh");
            var scriptContent = $"""
                #!/bin/bash
                echo "更新を適用しています..."
                sleep 2
                cp -f "{Path.GetDirectoryName(newExePath)}"/* "{currentDir}/"
                echo "更新が完了しました。"
                rm -rf "{tempDir}"
                """;
            await File.WriteAllTextAsync(scriptPath, scriptContent);

            // 実行権限を付与
            Process.Start(new ProcessStartInfo("chmod", $"+x \"{scriptPath}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            })?.WaitForExit();

            Process.Start(new ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = $"\"{scriptPath}\"",
                UseShellExecute = true
            });
        }

        Console.WriteLine("更新スクリプトを起動しました。このウィンドウは閉じてください。");
        Environment.Exit(0);
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.Add("User-Agent", $"DesignAid/{GetCurrentVersion()}");
        client.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");
        return client;
    }

    private static string FormatSize(long bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB"];
        var i = 0;
        double size = bytes;
        while (size >= 1024 && i < suffixes.Length - 1)
        {
            size /= 1024;
            i++;
        }
        return $"{size:F1} {suffixes[i]}";
    }

    // GitHub API レスポンス用のモデル
    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }

        [JsonPropertyName("prerelease")]
        public bool Prerelease { get; set; }

        [JsonPropertyName("assets")]
        public GitHubAsset[]? Assets { get; set; }
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("size")]
        public long Size { get; set; }

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; set; } = "";
    }
}
