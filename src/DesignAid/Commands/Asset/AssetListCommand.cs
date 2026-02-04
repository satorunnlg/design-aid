using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
using System.Text.Json;
using DesignAid.Infrastructure.FileSystem;

namespace DesignAid.Commands.Asset;

/// <summary>
/// 装置一覧を表示するコマンド。
/// </summary>
public class AssetListCommand : Command
{
    public AssetListCommand() : base("list", "装置一覧を表示")
    {
        this.Add(new Option<string?>("--project", "プロジェクトパス（省略時はカレントから検出）"));
        this.Add(new Option<bool>("--json", "JSON形式で出力"));

        this.Handler = CommandHandler.Create<string?, bool>(Execute);
    }

    private static void Execute(string? project, bool json)
    {
        var (resolvedPath, error) = CommandHelper.ResolveProjectPath(project);
        if (resolvedPath == null)
        {
            Console.Error.WriteLine($"[ERROR] {error}");
            Environment.ExitCode = 1;
            return;
        }

        var assetsDir = Path.Combine(resolvedPath, "assets");
        var assetJsonReader = new AssetJsonReader();
        var assets = new List<object>();

        if (Directory.Exists(assetsDir))
        {
            foreach (var dir in Directory.GetDirectories(assetsDir))
            {
                if (!assetJsonReader.Exists(dir)) continue;
                var assetJson = assetJsonReader.Read(dir);
                if (assetJson == null) continue;
                assets.Add(new { name = assetJson.Name, displayName = assetJson.DisplayName, id = assetJson.Id });
            }
        }

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(new { assets }, new JsonSerializerOptions { WriteIndented = true }));
        }
        else
        {
            if (assets.Count == 0)
            {
                Console.WriteLine("装置はありません");
                return;
            }
            Console.WriteLine("Assets:");
            foreach (dynamic a in assets)
            {
                Console.WriteLine($"  {a.name} ({a.displayName})");
                Console.WriteLine($"    ID: {a.id}");
            }
            Console.WriteLine($"\nTotal: {assets.Count} asset(s)");
        }
    }
}
