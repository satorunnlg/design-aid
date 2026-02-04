using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
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

        Directory.Delete(partPath, recursive: true);
        Console.WriteLine($"パーツを削除しました: {partNumber}");
    }
}
