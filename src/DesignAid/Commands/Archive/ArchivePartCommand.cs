using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
using System.IO.Compression;
using DesignAid.Infrastructure.FileSystem;

namespace DesignAid.Commands.Archive;

/// <summary>
/// パーツをアーカイブするコマンド。
/// </summary>
public class ArchivePartCommand : Command
{
    public ArchivePartCommand() : base("part", "パーツをアーカイブ")
    {
        this.Add(new Argument<string>("part-number", "アーカイブするパーツの型式"));

        this.Handler = CommandHandler.Create<string>(ExecuteAsync);
    }

    private static async Task ExecuteAsync(string partNumber)
    {
        var componentsDir = CommandHelper.GetComponentsDirectory();
        var partPath = Path.Combine(componentsDir, partNumber);

        // パーツの存在確認
        var partJsonReader = new PartJsonReader();
        if (!Directory.Exists(partPath) || !partJsonReader.Exists(partPath))
        {
            Console.Error.WriteLine($"[ERROR] パーツが見つかりません: {partNumber}");
            Environment.ExitCode = 1;
            return;
        }

        // 既にアーカイブ済みかチェック
        var indexPath = CommandHelper.GetArchiveIndexPath();
        var archiveIndexReader = new ArchiveIndexReader();
        var existingEntry = await archiveIndexReader.FindEntryAsync(indexPath, "part", partNumber);
        if (existingEntry != null)
        {
            Console.Error.WriteLine($"[ERROR] パーツは既にアーカイブされています: {partNumber}");
            Environment.ExitCode = 1;
            return;
        }

        // アーカイブディレクトリ作成
        var archiveDir = Path.Combine(CommandHelper.GetArchiveDirectory(), "components");
        Directory.CreateDirectory(archiveDir);
        var archivePath = Path.Combine(archiveDir, $"{partNumber}.zip");

        // part.json から名前を取得
        var partData = await partJsonReader.ReadAsync(partPath);
        var displayName = partData?.Name ?? partNumber;

        // 元のサイズを計算
        var originalSize = CalculateDirectorySize(partPath);

        Console.WriteLine($"Archiving part: {partNumber}");
        Console.WriteLine($"  Source: {partPath}");
        Console.WriteLine($"  Target: {archivePath}");

        try
        {
            // ZIP圧縮
            if (File.Exists(archivePath))
            {
                File.Delete(archivePath);
            }
            ZipFile.CreateFromDirectory(partPath, archivePath, CompressionLevel.Optimal, false);

            var archiveSize = new FileInfo(archivePath).Length;

            // インデックスに追加
            var entry = new ArchiveEntry
            {
                Type = "part",
                Name = partNumber,
                DisplayName = displayName,
                ArchivedAt = DateTime.UtcNow,
                OriginalPath = partPath,
                ArchivePath = archivePath,
                OriginalSizeBytes = originalSize,
                ArchiveSizeBytes = archiveSize,
                VectorIds = null // 将来的にベクトルインデックスのIDを保持
            };
            await archiveIndexReader.AddEntryAsync(indexPath, entry);

            // 元ディレクトリを削除
            Directory.Delete(partPath, true);

            var savedPercent = originalSize > 0 ? (1.0 - (double)archiveSize / originalSize) * 100 : 0;

            Console.WriteLine();
            Console.WriteLine($"Part archived: {partNumber}");
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
