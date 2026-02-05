using System.CommandLine;

namespace DesignAid.Commands.Archive;

/// <summary>
/// アーカイブ管理コマンドのルート。
/// </summary>
public class ArchiveCommand : Command
{
    public ArchiveCommand() : base("archive", "アーカイブ管理（容量節約）")
    {
        this.Add(new ArchiveAssetCommand());
        this.Add(new ArchivePartCommand());
        this.Add(new ArchiveListCommand());
        this.Add(new ArchiveRestoreCommand());
    }
}
