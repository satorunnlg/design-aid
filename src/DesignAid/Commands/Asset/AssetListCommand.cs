using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
using System.Text.Json;
using DesignAid.Infrastructure.FileSystem;

namespace DesignAid.Commands.Asset;

/// <summary>
/// 装置一覧を表示するコマンド。
/// </summary>
public class AssetListCommand : Command
{
    public AssetListCommand() : base("list", "装置一覧を表示")
    {
        this.Add(new Option<bool>("--json", "JSON形式で出力"));
        this.Add(new Option<bool>("--verbose", () => false, "詳細表示（紐付きパーツ、子装置を含む）"));

        this.Handler = CommandHandler.Create<bool, bool>(Execute);
    }

    private static void Execute(bool json, bool verbose)
    {
        var assetsDir = CommandHelper.GetAssetsDirectory();
        var componentsDir = CommandHelper.GetComponentsDirectory();
        var assetJsonReader = new AssetJsonReader();
        var partJsonReader = new PartJsonReader();
        var assets = new List<AssetInfo>();

        if (Directory.Exists(assetsDir))
        {
            foreach (var dir in Directory.GetDirectories(assetsDir))
            {
                if (!assetJsonReader.Exists(dir)) continue;
                var assetJson = assetJsonReader.Read(dir);
                if (assetJson == null) continue;

                var assetInfo = new AssetInfo
                {
                    Name = assetJson.Name,
                    DisplayName = assetJson.DisplayName ?? assetJson.Name,
                    Id = assetJson.Id,
                    Path = dir,
                    LinkedParts = new List<LinkedPartInfo>(),
                    ChildAssets = new List<ChildAssetInfo>()
                };

                // 紐付きパーツを取得（asset_links.json から）
                var linksPath = Path.Combine(dir, "asset_links.json");
                if (File.Exists(linksPath))
                {
                    try
                    {
                        var linksJson = File.ReadAllText(linksPath);
                        var links = JsonSerializer.Deserialize<AssetLinksJson>(linksJson, new JsonSerializerOptions
                        {
                            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
                        });

                        if (links?.Parts != null)
                        {
                            foreach (var partLink in links.Parts)
                            {
                                var partPath = Path.Combine(componentsDir, partLink.PartNumber);
                                var partJson = partJsonReader.Read(partPath);

                                assetInfo.LinkedParts.Add(new LinkedPartInfo
                                {
                                    PartNumber = partLink.PartNumber,
                                    Name = partJson?.Name ?? "(不明)",
                                    Quantity = partLink.Quantity
                                });
                            }
                        }

                        if (links?.ChildAssets != null)
                        {
                            foreach (var childLink in links.ChildAssets)
                            {
                                var childPath = Path.Combine(assetsDir, childLink.ChildAssetName);
                                var childJson = assetJsonReader.Read(childPath);

                                assetInfo.ChildAssets.Add(new ChildAssetInfo
                                {
                                    Name = childLink.ChildAssetName,
                                    DisplayName = childJson?.DisplayName ?? childLink.ChildAssetName,
                                    Quantity = childLink.Quantity,
                                    Notes = childLink.Notes
                                });
                            }
                        }
                    }
                    catch (JsonException)
                    {
                        // リンクファイルの読み込みに失敗した場合は無視
                    }
                }

                assets.Add(assetInfo);
            }
        }

        // 名前順にソート
        assets = assets.OrderBy(a => a.Name).ToList();

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(new { assets }, new JsonSerializerOptions { WriteIndented = true }));
        }
        else
        {
            if (assets.Count == 0)
            {
                Console.WriteLine("装置はありません");
                return;
            }
            Console.WriteLine("Assets:");
            foreach (var a in assets)
            {
                Console.WriteLine($"  {a.Name} ({a.DisplayName})");
                Console.WriteLine($"    ID: {a.Id}");

                if (verbose)
                {
                    // 子装置を表示
                    if (a.ChildAssets.Count > 0)
                    {
                        Console.WriteLine($"    Child Assets:");
                        foreach (var child in a.ChildAssets)
                        {
                            var notes = string.IsNullOrEmpty(child.Notes) ? "" : $" - {child.Notes}";
                            Console.WriteLine($"      - {child.Name} ({child.DisplayName}) x{child.Quantity}{notes}");
                        }
                    }

                    // 紐付きパーツを表示
                    if (a.LinkedParts.Count > 0)
                    {
                        Console.WriteLine($"    Linked Parts:");
                        foreach (var part in a.LinkedParts)
                        {
                            Console.WriteLine($"      - {part.PartNumber} ({part.Name}) x{part.Quantity}");
                        }
                    }

                    if (a.ChildAssets.Count == 0 && a.LinkedParts.Count == 0)
                    {
                        Console.WriteLine($"    (紐付きなし)");
                    }
                }
            }
            Console.WriteLine($"\nTotal: {assets.Count} asset(s)");
        }
    }

    // 内部データクラス
    private class AssetInfo
    {
        public string Name { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public Guid Id { get; set; }
        public string Path { get; set; } = string.Empty;
        public List<LinkedPartInfo> LinkedParts { get; set; } = new();
        public List<ChildAssetInfo> ChildAssets { get; set; } = new();
    }

    private class LinkedPartInfo
    {
        public string PartNumber { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int Quantity { get; set; }
    }

    private class ChildAssetInfo
    {
        public string Name { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public string? Notes { get; set; }
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
