using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
using DesignAid.Infrastructure.FileSystem;

namespace DesignAid.Commands.Asset;

/// <summary>
/// 装置を削除するコマンド。
/// </summary>
public class AssetRemoveCommand : Command
{
    public AssetRemoveCommand() : base("remove", "装置を削除")
    {
        this.Add(new Argument<string>("name", "装置名"));
        this.Add(new Option<string?>("--project", "プロジェクトパス（省略時はカレントから検出）"));
        this.Add(new Option<bool>("--force", "確認なしで実行"));

        this.Handler = CommandHandler.Create<string, string?, bool>(Execute);
    }

    private static void Execute(string name, string? project, bool force)
    {
        var (resolvedPath, error) = CommandHelper.ResolveProjectPath(project);
        if (resolvedPath == null)
        {
            Console.Error.WriteLine($"[ERROR] {error}");
            Environment.ExitCode = 1;
            return;
        }

        var assetPath = Path.Combine(resolvedPath, "assets", name);

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

        Directory.Delete(assetPath, recursive: true);
        Console.WriteLine($"装置を削除しました: {name}");
    }
}
