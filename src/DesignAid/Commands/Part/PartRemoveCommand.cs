using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
using DesignAid.Domain.ValueObjects;
using DesignAid.Infrastructure.FileSystem;

namespace DesignAid.Commands.Part;

/// <summary>
/// パーツを削除するコマンド。
/// </summary>
public class PartRemoveCommand : Command
{
    public PartRemoveCommand() : base("remove", "パーツを削除")
    {
        this.Add(new Argument<string>("part-number", "型式"));
        this.Add(new Option<bool>("--force", "確認なしで実行"));

        this.Handler = CommandHandler.Create<string, bool>(Execute);
    }

    private static void Execute(string partNumber, bool force)
    {
        if (CommandHelper.EnsureDataDirectory() == null) return;
        var partPath = Path.Combine(CommandHelper.GetComponentsDirectory(), partNumber);
        if (!Directory.Exists(partPath))
        {
            Console.Error.WriteLine($"[ERROR] パーツが見つかりません: {partNumber}");
            Environment.ExitCode = 1;
            return;
        }

        if (!force)
        {
            Console.Write($"パーツ '{partNumber}' を削除しますか？ [y/N] ");
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
            var dbPart = db.Parts.FirstOrDefault(p => p.PartNumber == new PartNumber(partNumber));
            if (dbPart != null)
            {
                // 関連する中間テーブルも削除
                var components = db.AssetComponents.Where(c => c.PartId == dbPart.Id);
                db.AssetComponents.RemoveRange(components);
                db.Parts.Remove(dbPart);
                db.SaveChanges();
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[WARN] DB 削除に失敗しました: {ex.Message}");
        }

        Directory.Delete(partPath, recursive: true);
        Console.WriteLine($"パーツを削除しました: {partNumber}");
    }
}
