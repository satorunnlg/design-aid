using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
using System.Text.Json;
using DesignAid.Infrastructure.FileSystem;

namespace DesignAid.Commands.Part;

/// <summary>
/// パーツを装置に紐付けるコマンド。
/// </summary>
public class PartLinkCommand : Command
{
    public PartLinkCommand() : base("link", "パーツを装置に紐付け")
    {
        this.Add(new Argument<string>("part-number", "型式"));
        this.Add(new Option<string>("--asset", "装置名") { IsRequired = true });
        this.Add(new Option<int>("--quantity", () => 1, "数量"));

        this.Handler = CommandHandler.Create<string, string, int>(Execute);
    }

    private static void Execute(string partNumber, string asset, int quantity)
    {
        if (CommandHelper.EnsureDataDirectory() == null) return;
        var assetsDir = CommandHelper.GetAssetsDirectory();
        var assetPath = Path.Combine(assetsDir, asset);
        var assetJsonReader = new AssetJsonReader();
        if (!assetJsonReader.Exists(assetPath))
        {
            Console.Error.WriteLine($"[ERROR] 装置が見つかりません: {asset}");
            Environment.ExitCode = 1;
            return;
        }

        var partPath = Path.Combine(CommandHelper.GetComponentsDirectory(), partNumber);
        var partJsonReader = new PartJsonReader();
        if (!partJsonReader.Exists(partPath))
        {
            Console.Error.WriteLine($"[ERROR] パーツが見つかりません: {partNumber}");
            Environment.ExitCode = 1;
            return;
        }

        // asset_links.json を読み込み（または新規作成）
        var linksPath = Path.Combine(assetPath, "asset_links.json");
        var links = new AssetLinksJson { Parts = new List<PartLinkEntry>(), ChildAssets = new List<ChildAssetEntry>() };

        if (File.Exists(linksPath))
        {
            try
            {
                var existingJson = File.ReadAllText(linksPath);
                links = JsonSerializer.Deserialize<AssetLinksJson>(existingJson, JsonOptions) ?? links;
                links.Parts ??= new List<PartLinkEntry>();
                links.ChildAssets ??= new List<ChildAssetEntry>();
            }
            catch (JsonException)
            {
                // ファイルが壊れている場合は新規作成
            }
        }

        // 既存のパーツリンクチェック
        var existingPart = links.Parts!.FirstOrDefault(p =>
            p.PartNumber.Equals(partNumber, StringComparison.OrdinalIgnoreCase));

        if (existingPart != null)
        {
            // 既存の場合は数量を更新
            existingPart.Quantity = quantity;
            Console.WriteLine($"Part link updated: {partNumber} in {asset}");
            Console.WriteLine($"  Quantity: {quantity}");
        }
        else
        {
            // 新規追加
            links.Parts!.Add(new PartLinkEntry
            {
                PartNumber = partNumber,
                Quantity = quantity
            });
            Console.WriteLine($"Part linked: {partNumber} -> {asset}");
            Console.WriteLine($"  Quantity: {quantity}");
        }

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
