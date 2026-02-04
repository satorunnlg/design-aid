using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
using DesignAid.Infrastructure.FileSystem;

namespace DesignAid.Commands.Project;

/// <summary>
/// プロジェクトを登録するコマンド。
/// </summary>
public class ProjectAddCommand : Command
{
    public ProjectAddCommand() : base("add", "プロジェクトを登録")
    {
        this.Add(new Argument<string>("path", "プロジェクトディレクトリのパス"));
        this.Add(new Option<string?>("--name", "プロジェクト名（省略時はディレクトリ名）"));
        this.Add(new Option<bool>("--create", "ディレクトリが存在しない場合に作成"));

        this.Handler = CommandHandler.Create<string, string?, bool>(ExecuteAsync);
    }

    private static async Task ExecuteAsync(string path, string? name, bool create)
    {
        var fullPath = Path.GetFullPath(path);

        if (create)
        {
            if (Directory.Exists(fullPath))
            {
                Console.Error.WriteLine($"[ERROR] ディレクトリは既に存在します: {fullPath}");
                Environment.ExitCode = 1;
                return;
            }
            Directory.CreateDirectory(fullPath);
            Console.WriteLine($"ディレクトリを作成しました: {fullPath}");
        }
        else if (!Directory.Exists(fullPath))
        {
            Console.Error.WriteLine($"[ERROR] ディレクトリが見つかりません: {fullPath}");
            Environment.ExitCode = 1;
            return;
        }

        var projectMarkerService = new ProjectMarkerService();
        if (projectMarkerService.Exists(fullPath))
        {
            Console.Error.WriteLine($"[ERROR] プロジェクトは既に登録されています: {fullPath}");
            Environment.ExitCode = 1;
            return;
        }

        var projectName = name ?? Path.GetFileName(fullPath);
        var projectId = Guid.NewGuid();
        await projectMarkerService.CreateAsync(fullPath, projectId, projectName);

        var assetsDir = Path.Combine(fullPath, "assets");
        if (!Directory.Exists(assetsDir))
            Directory.CreateDirectory(assetsDir);

        Console.WriteLine();
        Console.WriteLine($"Project registered: {projectName}");
        Console.WriteLine($"  Path: {fullPath}");
        Console.WriteLine($"  ID: {projectId}");
    }
}
