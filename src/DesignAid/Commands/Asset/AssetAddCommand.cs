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
        this.Add(new Option<string?>("--project", "プロジェクトパス（省略時はカレントから検出）"));
        this.Add(new Option<string?>("--display-name", "表示名"));

        this.Handler = CommandHandler.Create<string, string?, string?>(ExecuteAsync);
    }

    private static async Task ExecuteAsync(string name, string? project, string? displayName)
    {
        var (resolvedPath, error) = CommandHelper.ResolveProjectPath(project);
        if (resolvedPath == null)
        {
            Console.Error.WriteLine($"[ERROR] {error}");
            Environment.ExitCode = 1;
            return;
        }

        var assetPath = Path.Combine(resolvedPath, "assets", name);
        var assetJsonReader = new AssetJsonReader();
        if (Directory.Exists(assetPath) && assetJsonReader.Exists(assetPath))
        {
            Console.Error.WriteLine($"[ERROR] 装置は既に存在します: {name}");
            Environment.ExitCode = 1;
            return;
        }

        Directory.CreateDirectory(assetPath);
        var assetId = Guid.NewGuid();
        await assetJsonReader.CreateAsync(assetPath, assetId, name, displayName ?? name, "");

        Console.WriteLine();
        Console.WriteLine($"Asset created: {name}");
        Console.WriteLine($"  Path: {assetPath}");
        Console.WriteLine($"  ID: {assetId}");
    }
}
