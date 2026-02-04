using System.CommandLine;

namespace DesignAid.Commands.Project;

/// <summary>
/// プロジェクト管理コマンドのルート。
/// </summary>
public class ProjectCommand : Command
{
    public ProjectCommand() : base("project", "プロジェクト管理")
    {
        this.Add(new ProjectAddCommand());
        this.Add(new ProjectListCommand());
        this.Add(new ProjectRemoveCommand());
    }
}
