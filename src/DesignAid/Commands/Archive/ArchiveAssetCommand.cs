using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
using System.IO.Compression;
using DesignAid.Infrastructure.FileSystem;

namespace DesignAid.Commands.Archive;

/// <summary>
/// 装置をアーカイブするコマンド。
/// 単一装置、Dormant 一括、リストファイル指定に対応。
/// </summary>
public class ArchiveAssetCommand : Command
{
    public ArchiveAssetCommand() : base("asset", "装置をアーカイブ")
    {
        this.Add(new Argument<string?>("name", () => null, "アーカイブする装置名"));
        this.Add(new Option<bool>("--all-dormant", "Dormant ステータスの装置を全てアーカイブ"));
        this.Add(new Option<string?>("--list", "装置名リストファイルからアーカイブ（1行1装置名）"));
        this.Add(new Option<bool>("--yes", "確認をスキップ"));

        this.Handler = CommandHandler.Create<string?, bool, string?, bool>(ExecuteAsync);
    }

    private static async Task ExecuteAsync(string? name, bool allDormant, string? list, bool yes)
    {
        if (CommandHelper.EnsureDataDirectory() == null) return;

        // 引数バリデーション
        var modes = new[] { name != null, allDormant, list != null };
        if (modes.Count(m => m) != 1)
        {
            Console.Error.WriteLine("[ERROR] 装置名、--all-dormant、--list のいずれか1つを指定してください");
            Environment.ExitCode = 2;
            return;
        }

        var assetsDir = CommandHelper.GetAssetsDirectory();
        var assetJsonReader = new AssetJsonReader();
        var targetNames = new List<string>();

        if (name != null)
        {
            targetNames.Add(name);
        }
        else if (allDormant)
        {
            // Dormant ステータスの装置を収集
            if (Directory.Exists(assetsDir))
            {
                foreach (var dir in Directory.GetDirectories(assetsDir))
                {
                    if (!assetJsonReader.Exists(dir)) continue;
                    var assetJson = assetJsonReader.Read(dir);
                    if (assetJson == null) continue;

                    if ("dormant".Equals(assetJson.Status, StringComparison.OrdinalIgnoreCase))
                    {
                        targetNames.Add(assetJson.Name);
                    }
                }
            }

            if (targetNames.Count == 0)
            {
                Console.WriteLine("Dormant ステータスの装置はありません");
                return;
            }
        }
        else if (list != null)
        {
            if (!File.Exists(list))
            {
                Console.Error.WriteLine($"[ERROR] リストファイルが見つかりません: {list}");
                Environment.ExitCode = 1;
                return;
            }

            targetNames = File.ReadAllLines(list)
                .Select(l => l.Trim())
                .Where(l => !string.IsNullOrEmpty(l) && !l.StartsWith('#'))
                .ToList();

            if (targetNames.Count == 0)
            {
                Console.WriteLine("リストファイルに有効な装置名がありません");
                return;
            }
        }

        // 確認プロンプト（--yes でスキップ）
        if (!yes && targetNames.Count > 1)
        {
            Console.WriteLine($"以下の {targetNames.Count} 件の装置をアーカイブします:");
            foreach (var n in targetNames)
            {
                Console.WriteLine($"  - {n}");
            }
            Console.Write("\n続行しますか？ (y/N): ");
            var response = Console.ReadLine();
            if (!"y".Equals(response, StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("アーカイブをキャンセルしました");
                return;
            }
        }

        // 各装置をアーカイブ
        var successCount = 0;
        var failCount = 0;

        foreach (var targetName in targetNames)
        {
            var success = await ArchiveSingleAssetAsync(targetName, assetsDir, assetJsonReader);
            if (success)
                successCount++;
            else
                failCount++;
        }

        if (targetNames.Count > 1)
        {
            Console.WriteLine();
            Console.WriteLine($"Batch archive complete: {successCount} succeeded, {failCount} failed");
        }

        if (failCount > 0)
        {
            Environment.ExitCode = 1;
        }
    }

    /// <summary>
    /// 単一の装置をアーカイブする。
    /// </summary>
    private static async Task<bool> ArchiveSingleAssetAsync(
        string name, string assetsDir, AssetJsonReader assetJsonReader)
    {
        var assetPath = Path.Combine(assetsDir, name);

        // 装置の存在確認
        if (!Directory.Exists(assetPath) || !assetJsonReader.Exists(assetPath))
        {
            Console.Error.WriteLine($"[ERROR] 装置が見つかりません: {name}");
            return false;
        }

        // 既にアーカイブ済みかチェック
        var indexPath = CommandHelper.GetArchiveIndexPath();
        var archiveIndexReader = new ArchiveIndexReader();
        var existingEntry = await archiveIndexReader.FindEntryAsync(indexPath, "asset", name);
        if (existingEntry != null)
        {
            Console.Error.WriteLine($"[ERROR] 装置は既にアーカイブされています: {name}");
            return false;
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
                VectorIds = null
            };
            await archiveIndexReader.AddEntryAsync(indexPath, entry);

            // 元ディレクトリを削除（git の ReadOnly ファイル対策）
            CommandHelper.ForceDeleteDirectory(assetPath);

            var savedPercent = originalSize > 0 ? (1.0 - (double)archiveSize / originalSize) * 100 : 0;

            Console.WriteLine();
            Console.WriteLine($"Asset archived: {name}");
            Console.WriteLine($"  Original size: {FormatSize(originalSize)}");
            Console.WriteLine($"  Archive size: {FormatSize(archiveSize)}");
            Console.WriteLine($"  Saved: {savedPercent:F1}%");
            Console.WriteLine();
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ERROR] アーカイブに失敗しました ({name}): {ex.Message}");
            return false;
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
