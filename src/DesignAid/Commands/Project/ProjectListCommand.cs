using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
using System.Text.Json;
using DesignAid.Infrastructure.FileSystem;

namespace DesignAid.Commands.Project;

/// <summary>
/// プロジェクト一覧を表示するコマンド。
/// </summary>
public class ProjectListCommand : Command
{
    public ProjectListCommand() : base("list", "プロジェクト一覧を表示")
    {
        this.Add(new Option<string?>("--path", "検索パス（省略時はデフォルトパス）"));
        this.Add(new Option<bool>("--json", "JSON形式で出力"));

        this.Handler = CommandHandler.Create<string?, bool>(Execute);
    }

    private static void Execute(string? searchPath, bool json)
    {
        searchPath ??= CommandHelper.GetDefaultProjectsPath();

        if (!Directory.Exists(searchPath))
        {
            if (json)
                Console.WriteLine(JsonSerializer.Serialize(new { projects = Array.Empty<object>() }));
            else
                Console.WriteLine("登録済みプロジェクトはありません");
            return;
        }

        var projectMarkerService = new ProjectMarkerService();
        var assetJsonReader = new AssetJsonReader();
        var projects = new List<object>();

        foreach (var dir in Directory.GetDirectories(searchPath))
        {
            if (!projectMarkerService.Exists(dir)) continue;
            var marker = projectMarkerService.Read(dir);
            if (marker == null) continue;

            var assetsDir = Path.Combine(dir, "assets");
            var assetCount = Directory.Exists(assetsDir)
                ? Directory.GetDirectories(assetsDir).Count(d => assetJsonReader.Exists(d))
                : 0;

            projects.Add(new { name = marker.Name, path = dir, id = marker.ProjectId, assetCount });
        }

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(new { projects }, new JsonSerializerOptions { WriteIndented = true }));
        }
        else
        {
            if (projects.Count == 0)
            {
                Console.WriteLine("登録済みプロジェクトはありません");
                return;
            }

            Console.WriteLine("Registered Projects:");
            Console.WriteLine();
            foreach (dynamic p in projects)
            {
                Console.WriteLine($"  {p.name}");
                Console.WriteLine($"    Path: {p.path}");
                Console.WriteLine($"    ID: {p.id}");
                Console.WriteLine($"    Assets: {p.assetCount}");
                Console.WriteLine();
            }
            Console.WriteLine($"Total: {projects.Count} project(s)");
        }
    }
}
