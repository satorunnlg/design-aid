using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
using System.IO.Compression;
using DesignAid.Infrastructure.FileSystem;

namespace DesignAid.Commands.Archive;

/// <summary>
/// アーカイブから復元するコマンド。
/// </summary>
public class ArchiveRestoreCommand : Command
{
    public ArchiveRestoreCommand() : base("restore", "アーカイブから復元")
    {
        this.Add(new Argument<string>("type", "復元する種別 (asset/part)"));
        this.Add(new Argument<string>("name", "復元する名前"));

        this.Handler = CommandHandler.Create<string, string>(ExecuteAsync);
    }

    private static async Task ExecuteAsync(string type, string name)
    {
        // 種別の検証
        if (type != "asset" && type != "part")
        {
            Console.Error.WriteLine($"[ERROR] 不明な種別: {type}");
            Console.Error.WriteLine("有効な値: asset, part");
            Environment.ExitCode = 2;
            return;
        }

        if (CommandHelper.EnsureDataDirectory() == null) return;

        // アーカイブインデックスから検索
        var indexPath = CommandHelper.GetArchiveIndexPath();
        var archiveIndexReader = new ArchiveIndexReader();
        var entry = await archiveIndexReader.FindEntryAsync(indexPath, type, name);

        if (entry == null)
        {
            Console.Error.WriteLine($"[ERROR] アーカイブが見つかりません: {type} {name}");
            Environment.ExitCode = 1;
            return;
        }

        // アーカイブファイルの存在確認
        if (!File.Exists(entry.ArchivePath))
        {
            Console.Error.WriteLine($"[ERROR] アーカイブファイルが見つかりません: {entry.ArchivePath}");
            Environment.ExitCode = 1;
            return;
        }

        // 復元先の確認
        var targetDir = type == "asset"
            ? Path.Combine(CommandHelper.GetAssetsDirectory(), name)
            : Path.Combine(CommandHelper.GetComponentsDirectory(), name);

        if (Directory.Exists(targetDir))
        {
            Console.Error.WriteLine($"[ERROR] 復元先ディレクトリが既に存在します: {targetDir}");
            Environment.ExitCode = 1;
            return;
        }

        Console.WriteLine($"Restoring {type}: {name}");
        Console.WriteLine($"  Source: {entry.ArchivePath}");
        Console.WriteLine($"  Target: {targetDir}");

        try
        {
            // 親ディレクトリの確認・作成
            var parentDir = Path.GetDirectoryName(targetDir);
            if (!string.IsNullOrEmpty(parentDir))
            {
                Directory.CreateDirectory(parentDir);
            }

            // ZIP展開
            ZipFile.ExtractToDirectory(entry.ArchivePath, targetDir);

            // アーカイブファイルを削除
            File.Delete(entry.ArchivePath);

            // インデックスから削除
            await archiveIndexReader.RemoveEntryAsync(indexPath, type, name);

            Console.WriteLine();
            Console.WriteLine($"{(type == "asset" ? "Asset" : "Part")} restored: {name}");
            Console.WriteLine($"  Path: {targetDir}");
            Console.WriteLine($"  Size: {FormatSize(entry.OriginalSizeBytes)}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ERROR] 復元に失敗しました: {ex.Message}");
            Environment.ExitCode = 1;
        }
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
