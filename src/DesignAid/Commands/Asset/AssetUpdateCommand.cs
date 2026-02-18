using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
using DesignAid.Infrastructure.FileSystem;

namespace DesignAid.Commands.Asset;

/// <summary>
/// 装置情報を更新するコマンド。
/// asset.json のフィールドを上書きする。
/// </summary>
public class AssetUpdateCommand : Command
{
    public AssetUpdateCommand() : base("update", "装置情報を更新")
    {
        this.Add(new Argument<string>("name", "更新する装置名"));
        this.Add(new Option<string?>("--display-name", "表示名"));
        this.Add(new Option<string?>("--description", "説明"));

        this.Handler = CommandHandler.Create<string, string?, string?>(ExecuteAsync);
    }

    private static async Task ExecuteAsync(string name, string? displayName, string? description)
    {
        if (displayName == null && description == null)
        {
            Console.Error.WriteLine("[ERROR] 更新するフィールドを1つ以上指定してください（--display-name / --description）");
            Environment.ExitCode = 2;
            return;
        }

        if (CommandHelper.EnsureDataDirectory() == null) return;
        var assetsDir = CommandHelper.GetAssetsDirectory();
        var assetPath = Path.Combine(assetsDir, name);

        var assetJsonReader = new AssetJsonReader();
        if (!Directory.Exists(assetPath) || !assetJsonReader.Exists(assetPath))
        {
            Console.Error.WriteLine($"[ERROR] 装置が見つかりません: {name}");
            Environment.ExitCode = 1;
            return;
        }

        var assetJson = await assetJsonReader.ReadAsync(assetPath);
        if (assetJson == null)
        {
            Console.Error.WriteLine($"[ERROR] asset.json の読み込みに失敗しました: {name}");
            Environment.ExitCode = 1;
            return;
        }

        // 指定されたフィールドのみ上書き
        var updated = assetJson with
        {
            DisplayName = displayName ?? assetJson.DisplayName,
            Description = description ?? assetJson.Description
        };

        await assetJsonReader.WriteAsync(assetPath, updated);

        Console.WriteLine($"Asset updated: {name}");
        if (displayName != null) Console.WriteLine($"  DisplayName: {displayName}");
        if (description != null) Console.WriteLine($"  Description: {description}");
    }
}
