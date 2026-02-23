using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
using Microsoft.EntityFrameworkCore;

namespace DesignAid.Commands.Asset;

/// <summary>
/// 装置を削除するコマンド。
/// </summary>
public class AssetRemoveCommand : Command
{
    public AssetRemoveCommand() : base("remove", "装置を削除")
    {
        this.Add(new Argument<string>("name", "装置名"));
        this.Add(new Option<bool>("--force", "確認なしで実行"));

        this.Handler = CommandHandler.Create<string, bool>(Execute);
    }

    private static void Execute(string name, bool force)
    {
        if (CommandHelper.EnsureDataDirectory() == null) return;
        var assetsDir = CommandHelper.GetAssetsDirectory();
        var assetPath = Path.Combine(assetsDir, name);

        if (!Directory.Exists(assetPath))
        {
            Console.Error.WriteLine($"[ERROR] 装置が見つかりません: {name}");
            Environment.ExitCode = 1;
            return;
        }

        if (!force)
        {
            Console.Write($"装置 '{name}' を削除しますか？ [y/N] ");
            var response = Console.ReadLine()?.Trim().ToLowerInvariant();
            if (response != "y" && response != "yes")
            {
                Console.WriteLine("キャンセルしました");
                return;
            }
        }

        // DB からも削除
        try
        {
            using var db = CommandHelper.CreateDbContext();
            var dbAsset = db.Assets.FirstOrDefault(a => a.Name == name);
            if (dbAsset != null)
            {
                // 関連する中間テーブルも削除
                var subAssets = db.AssetSubAssets
                    .Where(s => s.ParentAssetId == dbAsset.Id || s.ChildAssetId == dbAsset.Id);
                db.AssetSubAssets.RemoveRange(subAssets);
                var components = db.AssetComponents.Where(c => c.AssetId == dbAsset.Id);
                db.AssetComponents.RemoveRange(components);
                db.Assets.Remove(dbAsset);
                db.SaveChanges();
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[WARN] DB 削除に失敗しました: {ex.Message}");
        }

        Directory.Delete(assetPath, recursive: true);
        Console.WriteLine($"装置を削除しました: {name}");
    }
}
