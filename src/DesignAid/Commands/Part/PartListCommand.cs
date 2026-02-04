using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
using System.Text.Json;
using DesignAid.Infrastructure.FileSystem;

namespace DesignAid.Commands.Part;

/// <summary>
/// パーツ一覧を表示するコマンド。
/// </summary>
public class PartListCommand : Command
{
    public PartListCommand() : base("list", "パーツ一覧を表示")
    {
        this.Add(new Option<bool>("--json", "JSON形式で出力"));

        this.Handler = CommandHandler.Create<bool>(Execute);
    }

    private static void Execute(bool json)
    {
        var componentsDir = CommandHelper.GetComponentsDirectory();
        var partJsonReader = new PartJsonReader();
        var parts = new List<object>();

        if (Directory.Exists(componentsDir))
        {
            foreach (var dir in Directory.GetDirectories(componentsDir))
            {
                if (!partJsonReader.Exists(dir)) continue;
                var partJson = partJsonReader.Read(dir);
                if (partJson == null) continue;
                var artifactCount = Directory.GetFiles(dir)
                    .Count(f => !Path.GetFileName(f).Equals("part.json", StringComparison.OrdinalIgnoreCase));
                parts.Add(new
                {
                    partNumber = partJson.PartNumber,
                    name = partJson.Name,
                    type = partJson.Type,
                    id = partJson.Id,
                    artifactCount
                });
            }
        }

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(new { parts }, new JsonSerializerOptions { WriteIndented = true }));
        }
        else
        {
            if (parts.Count == 0)
            {
                Console.WriteLine("パーツはありません");
                return;
            }
            Console.WriteLine("Shared Parts:");
            foreach (dynamic p in parts)
            {
                Console.WriteLine($"  {p.partNumber}");
                Console.WriteLine($"    Name: {p.name}");
                Console.WriteLine($"    Type: {p.type}");
                Console.WriteLine($"    Artifacts: {p.artifactCount} file(s)");
            }
            Console.WriteLine($"\nTotal: {parts.Count} part(s)");
        }
    }
}
