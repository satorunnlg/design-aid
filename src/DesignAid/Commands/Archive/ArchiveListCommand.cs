using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
using System.Text.Json;
using DesignAid.Infrastructure.FileSystem;

namespace DesignAid.Commands.Archive;

/// <summary>
/// アーカイブ一覧を表示するコマンド。
/// </summary>
public class ArchiveListCommand : Command
{
    public ArchiveListCommand() : base("list", "アーカイブ一覧を表示")
    {
        this.Add(new Option<bool>("--json", "JSON形式で出力"));

        this.Handler = CommandHandler.Create<bool>(ExecuteAsync);
    }

    private static async Task ExecuteAsync(bool json)
    {
        if (CommandHelper.EnsureDataDirectory() == null) return;
        var indexPath = CommandHelper.GetArchiveIndexPath();
        var archiveIndexReader = new ArchiveIndexReader();
        var index = await archiveIndexReader.LoadAsync(indexPath);

        if (json)
        {
            var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
            Console.WriteLine(JsonSerializer.Serialize(index, jsonOptions));
            return;
        }

        if (index.Archived.Count == 0)
        {
            Console.WriteLine("No archived items.");
            return;
        }

        Console.WriteLine("Archived items:");
        Console.WriteLine();

        // 装置
        var assets = index.Archived.Where(e => e.Type == "asset").ToList();
        if (assets.Count > 0)
        {
            Console.WriteLine("Assets:");
            foreach (var entry in assets)
            {
                Console.WriteLine($"  {entry.Name} ({entry.DisplayName})");
                Console.WriteLine($"    Archived: {entry.ArchivedAt:yyyy-MM-dd HH:mm}");
                Console.WriteLine($"    Size: {FormatSize(entry.ArchiveSizeBytes)} (saved {FormatSize(entry.OriginalSizeBytes - entry.ArchiveSizeBytes)})");
            }
            Console.WriteLine();
        }

        // パーツ
        var parts = index.Archived.Where(e => e.Type == "part").ToList();
        if (parts.Count > 0)
        {
            Console.WriteLine("Parts:");
            foreach (var entry in parts)
            {
                Console.WriteLine($"  {entry.Name} ({entry.DisplayName})");
                Console.WriteLine($"    Archived: {entry.ArchivedAt:yyyy-MM-dd HH:mm}");
                Console.WriteLine($"    Size: {FormatSize(entry.ArchiveSizeBytes)} (saved {FormatSize(entry.OriginalSizeBytes - entry.ArchiveSizeBytes)})");
            }
            Console.WriteLine();
        }

        var totalOriginal = index.Archived.Sum(e => e.OriginalSizeBytes);
        var totalArchive = index.Archived.Sum(e => e.ArchiveSizeBytes);
        Console.WriteLine($"Total: {index.Archived.Count} items, {FormatSize(totalArchive)} (saved {FormatSize(totalOriginal - totalArchive)})");
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 0) bytes = 0;
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
