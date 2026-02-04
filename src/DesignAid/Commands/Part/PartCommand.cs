using System.CommandLine;

namespace DesignAid.Commands.Part;

/// <summary>
/// パーツ管理コマンドのルート。
/// </summary>
public class PartCommand : Command
{
    public PartCommand() : base("part", "パーツ管理")
    {
        this.Add(new PartAddCommand());
        this.Add(new PartListCommand());
        this.Add(new PartRemoveCommand());
        this.Add(new PartLinkCommand());
    }
}
