using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
using DesignAid.Infrastructure.FileSystem;

namespace DesignAid.Commands.Asset;

/// <summary>
/// 装置を追加するコマンド。
/// </summary>
public class AssetAddCommand : Command
{
    public AssetAddCommand() : base("add", "装置を追加")
    {
        this.Add(new Argument<string>("name", "装置名"));
        this.Add(new Option<string?>("--display-name", "表示名"));
        this.Add(new Option<string?>("--description", "説明"));

        this.Handler = CommandHandler.Create<string, string?, string?>(ExecuteAsync);
    }

    private static async Task ExecuteAsync(string name, string? displayName, string? description)
    {
        var assetsDir = CommandHelper.GetAssetsDirectory();
        var assetPath = Path.Combine(assetsDir, name);

        var assetJsonReader = new AssetJsonReader();
        if (Directory.Exists(assetPath) && assetJsonReader.Exists(assetPath))
        {
            Console.Error.WriteLine($"[ERROR] 装置は既に存在します: {name}");
            Environment.ExitCode = 1;
            return;
        }

        // 装置ディレクトリを作成
        Directory.CreateDirectory(assetPath);

        // asset.json を作成
        var assetId = Guid.NewGuid();
        await assetJsonReader.CreateAsync(assetPath, assetId, name, displayName ?? name, description ?? "");

        Console.WriteLine();
        Console.WriteLine($"Asset created: {name}");
        Console.WriteLine($"  Path: {assetPath}");
        Console.WriteLine($"  ID: {assetId}");
    }
}
