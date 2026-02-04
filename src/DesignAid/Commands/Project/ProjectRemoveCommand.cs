using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
using DesignAid.Infrastructure.FileSystem;

namespace DesignAid.Commands.Project;

/// <summary>
/// プロジェクトを登録解除するコマンド。
/// </summary>
public class ProjectRemoveCommand : Command
{
    public ProjectRemoveCommand() : base("remove", "プロジェクトを登録解除")
    {
        this.Add(new Argument<string>("path", "プロジェクトディレクトリのパス"));
        this.Add(new Option<bool>("--delete", "ディレクトリも削除"));
        this.Add(new Option<bool>("--force", "確認なしで実行"));

        this.Handler = CommandHandler.Create<string, bool, bool>(Execute);
    }

    private static void Execute(string path, bool delete, bool force)
    {
        var fullPath = Path.GetFullPath(path);

        if (!Directory.Exists(fullPath))
        {
            Console.Error.WriteLine($"[ERROR] ディレクトリが見つかりません: {fullPath}");
            Environment.ExitCode = 1;
            return;
        }

        var projectMarkerService = new ProjectMarkerService();
        if (!projectMarkerService.Exists(fullPath))
        {
            Console.Error.WriteLine($"[ERROR] プロジェクトが登録されていません: {fullPath}");
            Environment.ExitCode = 1;
            return;
        }

        var marker = projectMarkerService.Read(fullPath);
        var projectName = marker?.Name ?? Path.GetFileName(fullPath);

        if (!force)
        {
            Console.Write($"プロジェクト '{projectName}' を登録解除しますか？");
            if (delete) Console.Write(" [ディレクトリも削除されます]");
            Console.Write(" [y/N] ");
            var response = Console.ReadLine()?.Trim().ToLowerInvariant();
            if (response != "y" && response != "yes")
            {
                Console.WriteLine("キャンセルしました");
                return;
            }
        }

        projectMarkerService.Delete(fullPath);
        if (delete)
        {
            Directory.Delete(fullPath, recursive: true);
            Console.WriteLine($"プロジェクトを削除しました: {projectName}");
        }
        else
        {
            Console.WriteLine($"プロジェクトを登録解除しました: {projectName}");
        }
    }
}
