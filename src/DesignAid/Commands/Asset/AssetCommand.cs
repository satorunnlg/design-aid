using System.CommandLine;

namespace DesignAid.Commands.Asset;

/// <summary>
/// 装置管理コマンドのルート。
/// </summary>
public class AssetCommand : Command
{
    public AssetCommand() : base("asset", "装置管理")
    {
        this.Add(new AssetAddCommand());
        this.Add(new AssetListCommand());
        this.Add(new AssetRemoveCommand());
        this.Add(new AssetLinkCommand());
        this.Add(new AssetUnlinkCommand());
    }
}
