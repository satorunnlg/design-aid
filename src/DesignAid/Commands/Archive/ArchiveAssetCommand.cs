using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
using System.IO.Compression;
using DesignAid.Infrastructure.FileSystem;

namespace DesignAid.Commands.Archive;

/// <summary>
/// 装置をアーカイブするコマンド。
/// </summary>
public class ArchiveAssetCommand : Command
{
    public ArchiveAssetCommand() : base("asset", "装置をアーカイブ")
    {
        this.Add(new Argument<string>("name", "アーカイブする装置名"));

        this.Handler = CommandHandler.Create<string>(ExecuteAsync);
    }

    private static async Task ExecuteAsync(string name)
    {
        var assetsDir = CommandHelper.GetAssetsDirectory();
        var assetPath = Path.Combine(assetsDir, name);

        // 装置の存在確認
        var assetJsonReader = new AssetJsonReader();
        if (!Directory.Exists(assetPath) || !assetJsonReader.Exists(assetPath))
        {
            Console.Error.WriteLine($"[ERROR] 装置が見つかりません: {name}");
            Environment.ExitCode = 1;
            return;
        }

        // 既にアーカイブ済みかチェック
        var indexPath = CommandHelper.GetArchiveIndexPath();
        var archiveIndexReader = new ArchiveIndexReader();
        var existingEntry = await archiveIndexReader.FindEntryAsync(indexPath, "asset", name);
        if (existingEntry != null)
        {
            Console.Error.WriteLine($"[ERROR] 装置は既にアーカイブされています: {name}");
            Environment.ExitCode = 1;
            return;
        }

        // アーカイブディレクトリ作成
        var archiveDir = Path.Combine(CommandHelper.GetArchiveDirectory(), "assets");
        Directory.CreateDirectory(archiveDir);
        var archivePath = Path.Combine(archiveDir, $"{name}.zip");

        // asset.json から表示名を取得
        var assetData = await assetJsonReader.ReadAsync(assetPath);
        var displayName = assetData?.DisplayName ?? name;

        // 元のサイズを計算
        var originalSize = CalculateDirectorySize(assetPath);

        Console.WriteLine($"Archiving asset: {name}");
        Console.WriteLine($"  Source: {assetPath}");
        Console.WriteLine($"  Target: {archivePath}");

        try
        {
            // ZIP圧縮
            if (File.Exists(archivePath))
            {
                File.Delete(archivePath);
            }
            ZipFile.CreateFromDirectory(assetPath, archivePath, CompressionLevel.Optimal, false);

            var archiveSize = new FileInfo(archivePath).Length;

            // インデックスに追加
            var entry = new ArchiveEntry
            {
                Type = "asset",
                Name = name,
                DisplayName = displayName,
                ArchivedAt = DateTime.UtcNow,
                OriginalPath = assetPath,
                ArchivePath = archivePath,
                OriginalSizeBytes = originalSize,
                ArchiveSizeBytes = archiveSize,
                VectorIds = null // 将来的にベクトルインデックスのIDを保持
            };
            await archiveIndexReader.AddEntryAsync(indexPath, entry);

            // 元ディレクトリを削除
            Directory.Delete(assetPath, true);

            var savedPercent = originalSize > 0 ? (1.0 - (double)archiveSize / originalSize) * 100 : 0;

            Console.WriteLine();
            Console.WriteLine($"Asset archived: {name}");
            Console.WriteLine($"  Original size: {FormatSize(originalSize)}");
            Console.WriteLine($"  Archive size: {FormatSize(archiveSize)}");
            Console.WriteLine($"  Saved: {savedPercent:F1}%");
            Console.WriteLine();
            Console.WriteLine("Note: ベクトルインデックスは検索用に保持されます。");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ERROR] アーカイブに失敗しました: {ex.Message}");
            Environment.ExitCode = 1;
        }
    }

    private static long CalculateDirectorySize(string path)
    {
        var dirInfo = new DirectoryInfo(path);
        return dirInfo.EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
    }

    private static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var size = (double)bytes;
        var unitIndex = 0;
        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }
        return $"{size:F1} {units[unitIndex]}";
    }
}
