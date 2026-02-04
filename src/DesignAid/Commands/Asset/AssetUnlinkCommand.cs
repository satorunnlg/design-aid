using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
using System.Text.Json;
using DesignAid.Infrastructure.FileSystem;

namespace DesignAid.Commands.Asset;

/// <summary>
/// 子装置のリンクを解除するコマンド。
/// </summary>
public class AssetUnlinkCommand : Command
{
    public AssetUnlinkCommand() : base("unlink", "子装置のリンクを解除")
    {
        this.Add(new Argument<string>("parent-asset", "親装置名"));
        this.Add(new Option<string>("--child", "子装置名") { IsRequired = true });

        this.Handler = CommandHandler.Create<string, string>(Execute);
    }

    private static void Execute(string parentAsset, string child)
    {
        var assetsDir = CommandHelper.GetAssetsDirectory();
        var assetJsonReader = new AssetJsonReader();

        // 親装置の存在チェック
        var parentPath = Path.Combine(assetsDir, parentAsset);
        if (!assetJsonReader.Exists(parentPath))
        {
            Console.Error.WriteLine($"[ERROR] 親装置が見つかりません: {parentAsset}");
            Environment.ExitCode = 1;
            return;
        }

        // asset_links.json を読み込み
        var linksPath = Path.Combine(parentPath, "asset_links.json");
        if (!File.Exists(linksPath))
        {
            Console.Error.WriteLine($"[ERROR] 子装置のリンクが存在しません: {child}");
            Environment.ExitCode = 1;
            return;
        }

        AssetLinksJson links;
        try
        {
            var existingJson = File.ReadAllText(linksPath);
            links = JsonSerializer.Deserialize<AssetLinksJson>(existingJson, JsonOptions)
                ?? new AssetLinksJson { Parts = new List<PartLinkEntry>(), ChildAssets = new List<ChildAssetEntry>() };
            links.ChildAssets ??= new List<ChildAssetEntry>();
        }
        catch (JsonException)
        {
            Console.Error.WriteLine("[ERROR] リンクファイルの読み込みに失敗しました");
            Environment.ExitCode = 1;
            return;
        }

        // 子装置のリンクを検索
        var existingChild = links.ChildAssets.FirstOrDefault(c =>
            c.ChildAssetName.Equals(child, StringComparison.OrdinalIgnoreCase));

        if (existingChild == null)
        {
            Console.Error.WriteLine($"[ERROR] 子装置のリンクが存在しません: {child}");
            Environment.ExitCode = 1;
            return;
        }

        // リンクを削除
        links.ChildAssets.Remove(existingChild);

        Console.WriteLine($"Child asset unlinked: {child} from {parentAsset}");

        // asset_links.json を保存
        var json = JsonSerializer.Serialize(links, JsonOptions);
        File.WriteAllText(linksPath, json);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    // データクラス
    private class AssetLinksJson
    {
        public List<PartLinkEntry>? Parts { get; set; }
        public List<ChildAssetEntry>? ChildAssets { get; set; }
    }

    private class PartLinkEntry
    {
        public string PartNumber { get; set; } = string.Empty;
        public int Quantity { get; set; }
    }

    private class ChildAssetEntry
    {
        public string ChildAssetName { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public string? Notes { get; set; }
    }
}
