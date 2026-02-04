using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
using DesignAid.Infrastructure.FileSystem;

namespace DesignAid.Commands.Part;

/// <summary>
/// パーツを装置に紐付けるコマンド。
/// </summary>
public class PartLinkCommand : Command
{
    public PartLinkCommand() : base("link", "パーツを装置に紐付け")
    {
        this.Add(new Argument<string>("part-number", "型式"));
        this.Add(new Option<string>("--asset", "装置名") { IsRequired = true });
        this.Add(new Option<string?>("--project", "プロジェクトパス（省略時はカレントから検出）"));
        this.Add(new Option<int>("--quantity", () => 1, "数量"));

        this.Handler = CommandHandler.Create<string, string, string?, int>(Execute);
    }

    private static void Execute(string partNumber, string asset, string? project, int quantity)
    {
        var (resolvedPath, error) = CommandHelper.ResolveProjectPath(project);
        if (resolvedPath == null)
        {
            Console.Error.WriteLine($"[ERROR] {error}");
            Environment.ExitCode = 1;
            return;
        }

        var assetPath = Path.Combine(resolvedPath, "assets", asset);
        var assetJsonReader = new AssetJsonReader();
        if (!assetJsonReader.Exists(assetPath))
        {
            Console.Error.WriteLine($"[ERROR] 装置が見つかりません: {asset}");
            Environment.ExitCode = 1;
            return;
        }

        var partPath = Path.Combine(CommandHelper.GetComponentsDirectory(), partNumber);
        var partJsonReader = new PartJsonReader();
        if (!partJsonReader.Exists(partPath))
        {
            Console.Error.WriteLine($"[ERROR] パーツが見つかりません: {partNumber}");
            Environment.ExitCode = 1;
            return;
        }

        // TODO: DB連携後、AssetComponents テーブルに紐付けを保存
        Console.WriteLine();
        Console.WriteLine($"Part linked: {partNumber} -> {asset}");
        Console.WriteLine($"  Quantity: {quantity}");
        Console.WriteLine();
        Console.WriteLine("[INFO] 注意: 現在はファイルベースの管理のみです。");
        Console.WriteLine("       DB連携後、AssetComponents テーブルで紐付けが永続化されます。");
    }
}
