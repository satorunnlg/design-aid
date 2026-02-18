using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
using System.Text;
using System.Text.Json;
using DesignAid.Infrastructure.FileSystem;

namespace DesignAid.Commands.Asset;

/// <summary>
/// BOM（部品表）と原価集計を表示するコマンド。
/// ファイルシステム（asset.json / asset_links.json / part.json）から読み込む。
/// </summary>
public class AssetBomCommand : Command
{
    public AssetBomCommand() : base("bom", "BOM（部品表）と原価集計を表示")
    {
        this.Add(new Argument<string>("name", "装置名"));
        this.Add(new Option<bool>("--json", "JSON形式で出力"));
        this.Add(new Option<string?>("--export", "エクスポート形式 (csv)"));
        this.Add(new Option<bool>("--include-subassets", "子装置のパーツも含める"));

        this.Handler = CommandHandler.Create<string, bool, string?, bool>(ExecuteAsync);
    }

    private static readonly JsonSerializerOptions JsonReadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private static async Task ExecuteAsync(string name, bool json, string? export, bool includeSubassets)
    {
        if (CommandHelper.EnsureDataDirectory() == null) return;
        var assetsDir = CommandHelper.GetAssetsDirectory();
        var componentsDir = CommandHelper.GetComponentsDirectory();
        var assetPath = Path.Combine(assetsDir, name);

        var assetJsonReader = new AssetJsonReader();
        if (!assetJsonReader.Exists(assetPath))
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

        // BOM エントリを収集
        var bomEntries = await CollectBomEntries(assetsDir, componentsDir, name, includeSubassets);

        if (export?.Equals("csv", StringComparison.OrdinalIgnoreCase) == true)
        {
            OutputCsv(bomEntries, name);
        }
        else if (json)
        {
            OutputJson(bomEntries, name, assetJson.DisplayName);
        }
        else
        {
            OutputTable(bomEntries, name, assetJson.DisplayName);
        }
    }

    /// <summary>
    /// BOM エントリを asset_links.json + part.json から再帰的に収集する。
    /// </summary>
    private static async Task<List<BomEntry>> CollectBomEntries(
        string assetsDir,
        string componentsDir,
        string assetName,
        bool includeSubassets)
    {
        var entries = new List<BomEntry>();
        var partJsonReader = new PartJsonReader();
        var assetPath = Path.Combine(assetsDir, assetName);
        var linksPath = Path.Combine(assetPath, "asset_links.json");

        if (!File.Exists(linksPath))
            return entries;

        // asset_links.json を読み込み
        AssetLinksJson? links;
        try
        {
            var linksJson = await File.ReadAllTextAsync(linksPath);
            links = JsonSerializer.Deserialize<AssetLinksJson>(linksJson, JsonReadOptions);
        }
        catch (JsonException)
        {
            return entries;
        }

        if (links == null) return entries;

        // パーツリンクを処理
        if (links.Parts != null)
        {
            foreach (var partLink in links.Parts)
            {
                var partPath = Path.Combine(componentsDir, partLink.PartNumber);
                var partJson = partJsonReader.Read(partPath);

                entries.Add(new BomEntry
                {
                    PartNumber = partLink.PartNumber,
                    Name = partJson?.Name ?? "(不明)",
                    Type = partJson?.Type ?? "Unknown",
                    Quantity = partLink.Quantity,
                    UnitPrice = partJson?.UnitPrice,
                    Currency = partJson?.Currency,
                    AssetSource = assetName
                });
            }
        }

        // 子装置のパーツも含める
        if (includeSubassets && links.ChildAssets != null)
        {
            foreach (var childLink in links.ChildAssets)
            {
                var childEntries = await CollectBomEntries(
                    assetsDir, componentsDir, childLink.ChildAssetName, includeSubassets);

                // 子装置の数量を乗算
                foreach (var childEntry in childEntries)
                {
                    childEntry.Quantity *= childLink.Quantity;
                    entries.Add(childEntry);
                }
            }
        }

        return entries;
    }

    private static void OutputTable(List<BomEntry> entries, string assetName, string? displayName)
    {
        var header = displayName != null
            ? $"BOM: {assetName} ({displayName})"
            : $"BOM: {assetName}";

        Console.WriteLine(header);
        Console.WriteLine();
        Console.WriteLine("  No. Part Number          Name                 Type         Qty   Unit Price  Subtotal");
        Console.WriteLine("  --- -------------------  -------------------  ----------   ----  ----------  --------");

        var totalCost = 0m;
        var pricedCount = 0;
        var totalPcs = 0;

        for (var i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            var priceStr = e.UnitPrice.HasValue ? $"¥{e.UnitPrice:N0}" : "-";
            var subtotal = e.UnitPrice.HasValue ? e.UnitPrice.Value * e.Quantity : (decimal?)null;
            var subtotalStr = subtotal.HasValue ? $"¥{subtotal:N0}" : "-";

            Console.WriteLine($"  {i + 1,3} {e.PartNumber,-20} {Truncate(e.Name, 20),-20} {e.Type,-12} {e.Quantity,4}  {priceStr,10}  {subtotalStr,8}");

            if (subtotal.HasValue)
            {
                totalCost += subtotal.Value;
                pricedCount++;
            }
            totalPcs += e.Quantity;
        }

        Console.WriteLine();
        Console.WriteLine($"Total parts: {entries.Count} ({totalPcs} pcs)");
        if (pricedCount > 0)
        {
            Console.WriteLine($"Total cost: ¥{totalCost:N0} ({pricedCount} priced part(s))");
        }
        else
        {
            Console.WriteLine("Total cost: - (価格未設定)");
        }
    }

    private static void OutputJson(List<BomEntry> entries, string assetName, string? displayName)
    {
        var totalCost = entries
            .Where(e => e.UnitPrice.HasValue)
            .Sum(e => e.UnitPrice!.Value * e.Quantity);

        Console.WriteLine(JsonSerializer.Serialize(new
        {
            asset = assetName,
            displayName,
            parts = entries.Select(e => new
            {
                partNumber = e.PartNumber,
                name = e.Name,
                type = e.Type,
                quantity = e.Quantity,
                unitPrice = e.UnitPrice,
                currency = e.Currency,
                subtotal = e.UnitPrice.HasValue ? e.UnitPrice.Value * e.Quantity : (decimal?)null,
                assetSource = e.AssetSource
            }),
            totalCost,
            totalParts = entries.Count
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void OutputCsv(List<BomEntry> entries, string assetName)
    {
        var sb = new StringBuilder();
        sb.AppendLine("PartNumber,Name,Type,Quantity,UnitPrice,Currency,Subtotal,AssetSource");

        foreach (var e in entries)
        {
            var subtotal = e.UnitPrice.HasValue ? (e.UnitPrice.Value * e.Quantity).ToString() : "";
            var price = e.UnitPrice?.ToString() ?? "";
            var currency = e.Currency ?? "";
            // CSV のフィールドをエスケープ
            sb.AppendLine($"{Escape(e.PartNumber)},{Escape(e.Name)},{e.Type},{e.Quantity},{price},{currency},{subtotal},{Escape(e.AssetSource)}");
        }

        Console.Write(sb.ToString());
    }

    private static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength) return value;
        return value[..(maxLength - 1)] + "…";
    }

    private static string Escape(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }
        return value;
    }

    private class BomEntry
    {
        public string PartNumber { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public decimal? UnitPrice { get; set; }
        public string? Currency { get; set; }
        public string AssetSource { get; set; } = string.Empty;
    }

    // asset_links.json のデータ構造
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
