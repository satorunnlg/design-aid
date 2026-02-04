using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
using System.Text.Json;
using DesignAid.Infrastructure.FileSystem;

namespace DesignAid.Commands.Asset;

/// <summary>
/// 子装置を親装置にリンクするコマンド（SubAsset）。
/// </summary>
public class AssetLinkCommand : Command
{
    public AssetLinkCommand() : base("link", "子装置を親装置にリンク")
    {
        this.Add(new Argument<string>("parent-asset", "親装置名"));
        this.Add(new Option<string>("--child", "子装置名") { IsRequired = true });
        this.Add(new Option<int>("--quantity", () => 1, "数量"));
        this.Add(new Option<string?>("--notes", "備考"));

        this.Handler = CommandHandler.Create<string, string, int, string?>(Execute);
    }

    private static void Execute(string parentAsset, string child, int quantity, string? notes)
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

        // 子装置の存在チェック
        var childPath = Path.Combine(assetsDir, child);
        if (!assetJsonReader.Exists(childPath))
        {
            Console.Error.WriteLine($"[ERROR] 子装置が見つかりません: {child}");
            Environment.ExitCode = 1;
            return;
        }

        // 自分自身へのリンク禁止
        if (parentAsset.Equals(child, StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("[ERROR] 自分自身を子装置として追加できません");
            Environment.ExitCode = 1;
            return;
        }

        // asset_links.json を読み込み（または新規作成）
        var linksPath = Path.Combine(parentPath, "asset_links.json");
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

        // 既存の子装置チェック
        var existingChild = links.ChildAssets!.FirstOrDefault(c =>
            c.ChildAssetName.Equals(child, StringComparison.OrdinalIgnoreCase));

        if (existingChild != null)
        {
            // 既存の場合は数量を更新
            existingChild.Quantity = quantity;
            existingChild.Notes = notes;
            Console.WriteLine($"Child asset updated: {child} in {parentAsset}");
            Console.WriteLine($"  Quantity: {quantity}");
        }
        else
        {
            // 新規追加
            links.ChildAssets!.Add(new ChildAssetEntry
            {
                ChildAssetName = child,
                Quantity = quantity,
                Notes = notes
            });
            Console.WriteLine($"Child asset linked: {child} -> {parentAsset}");
            Console.WriteLine($"  Quantity: {quantity}");
        }

        if (!string.IsNullOrEmpty(notes))
        {
            Console.WriteLine($"  Notes: {notes}");
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
