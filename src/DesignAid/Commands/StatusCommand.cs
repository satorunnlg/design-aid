using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
using System.Text.Json;
using DesignAid.Infrastructure.Embedding;
using DesignAid.Infrastructure.FileSystem;
using DesignAid.Infrastructure.Persistence;
using DesignAid.Infrastructure.VectorSearch;
using Microsoft.EntityFrameworkCore;

namespace DesignAid.Commands;

/// <summary>
/// システム状態を表示するコマンド。
/// </summary>
public class StatusCommand : Command
{
    public StatusCommand() : base("status", "システム状態を表示")
    {
        this.Add(new Option<string?>("--asset", "特定装置を表示"));
        this.Add(new Option<bool>("--json", "JSON形式で出力"));

        this.Handler = CommandHandler.Create<string?, bool>(Execute);
    }

    private static void Execute(string? asset, bool json)
    {
        var dataDir = CommandHelper.GetDataDirectory();
        var assetsDir = CommandHelper.GetAssetsDirectory();
        var componentsDir = CommandHelper.GetComponentsDirectory();
        var dbPath = CommandHelper.GetDatabasePath();

        var assetJsonReader = new AssetJsonReader();
        var partJsonReader = new PartJsonReader();

        // 装置情報を収集
        var assets = new List<AssetInfo>();
        if (Directory.Exists(assetsDir))
        {
            foreach (var dir in Directory.GetDirectories(assetsDir))
            {
                if (!assetJsonReader.Exists(dir)) continue;
                var assetJson = assetJsonReader.Read(dir);
                if (assetJson == null) continue;

                // 特定装置が指定された場合はフィルタ
                if (!string.IsNullOrEmpty(asset) &&
                    !assetJson.Name.Equals(asset, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                assets.Add(new AssetInfo
                {
                    Name = assetJson.Name,
                    DisplayName = assetJson.DisplayName,
                    Id = assetJson.Id,
                    Path = dir
                });
            }
        }

        // 共有パーツ情報を収集
        var parts = new List<PartInfo>();
        if (Directory.Exists(componentsDir))
        {
            foreach (var partDir in Directory.GetDirectories(componentsDir))
            {
                if (!partJsonReader.Exists(partDir)) continue;
                var partJson = partJsonReader.Read(partDir);
                if (partJson == null) continue;

                parts.Add(new PartInfo
                {
                    PartNumber = partJson.PartNumber,
                    Name = partJson.Name,
                    Type = partJson.Type,
                    Id = partJson.Id
                });
            }
        }

        // ベクトルインデックス状態
        var vectorStatus = CheckVectorIndex();

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                system = new
                {
                    dataDirectory = dataDir,
                    database = dbPath,
                    databaseExists = File.Exists(dbPath),
                    vectorIndex = vectorStatus
                },
                assets,
                components = parts,
                summary = new
                {
                    assetCount = assets.Count,
                    componentCount = parts.Count
                }
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        else
        {
            Console.WriteLine("Design Aid Status");
            Console.WriteLine();

            // システム情報
            Console.WriteLine("System:");
            Console.WriteLine($"  Data Directory: {dataDir}");
            Console.WriteLine($"  Database: {dbPath} {(File.Exists(dbPath) ? "(exists)" : "(not created)")}");
            Console.WriteLine($"  Vector Index: {vectorStatus}");
            Console.WriteLine();

            // 装置情報
            if (assets.Count > 0)
            {
                Console.WriteLine($"Assets: {assets.Count}");
                foreach (var assetInfo in assets)
                {
                    Console.WriteLine($"  {assetInfo.Name} ({assetInfo.DisplayName})");
                    Console.WriteLine($"    ID: {assetInfo.Id}");
                    Console.WriteLine($"    Path: {assetInfo.Path}");
                }
                Console.WriteLine();
            }
            else
            {
                Console.WriteLine("Assets: (none)");
                Console.WriteLine();
            }

            // 共有パーツ情報
            Console.WriteLine($"Components: {parts.Count}");
            if (parts.Count > 0)
            {
                var byType = parts.GroupBy(p => p.Type);
                foreach (var group in byType)
                {
                    Console.WriteLine($"  {group.Key}: {group.Count()}");
                }
            }
            Console.WriteLine();

            // サマリー
            Console.WriteLine("Summary:");
            Console.WriteLine($"  Assets: {assets.Count}");
            Console.WriteLine($"  Components: {parts.Count}");
        }
    }

    private static string CheckVectorIndex()
    {
        var settings = CommandHelper.LoadSettings();

        if (!settings.GetBool("vector_search.enabled", true))
        {
            return "Disabled";
        }

        try
        {
            var dbPath = CommandHelper.GetDatabasePath();
            if (!File.Exists(dbPath))
            {
                return "Not initialized (DB not found)";
            }

            var dimensions = settings.GetInt("embedding.dimensions", 384);
            var embeddingProvider = new MockEmbeddingProvider(dimensions);

            var optionsBuilder = new DbContextOptionsBuilder<DesignAidDbContext>();
            optionsBuilder.UseSqlite($"Data Source={dbPath}");
            using var context = new DesignAidDbContext(optionsBuilder.Options);

            var dataDir = CommandHelper.GetDataDirectory();
            var hnswIndexPath = Path.Combine(dataDir,
                settings.Get("vector_search.hnsw_index_path", "hnsw_index.bin")!);
            using var vectorService = new VectorSearchService(context, embeddingProvider, hnswIndexPath);

            var pointCount = vectorService.GetPointCountAsync().GetAwaiter().GetResult();
            if (pointCount == 0)
            {
                return "Empty (0 vectors)";
            }

            return $"{pointCount} vectors ({dimensions} dimensions)";
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    private class AssetInfo
    {
        public string Name { get; set; } = string.Empty;
        public string? DisplayName { get; set; }
        public Guid Id { get; set; }
        public string Path { get; set; } = string.Empty;
    }

    private class PartInfo
    {
        public string PartNumber { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public Guid Id { get; set; }
    }
}
