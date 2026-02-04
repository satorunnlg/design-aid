using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
using System.Text.Json;
using DesignAid.Infrastructure.Embedding;
using DesignAid.Infrastructure.FileSystem;
using DesignAid.Infrastructure.Qdrant;

namespace DesignAid.Commands;

/// <summary>
/// システム状態を表示するコマンド。
/// </summary>
public class StatusCommand : Command
{
    public StatusCommand() : base("status", "プロジェクト状態を表示")
    {
        this.Add(new Option<string?>("--project", "特定プロジェクトを表示"));
        this.Add(new Option<string?>("--asset", "特定装置を表示"));
        this.Add(new Option<bool>("--json", "JSON形式で出力"));

        this.Handler = CommandHandler.Create<string?, string?, bool>(Execute);
    }

    private static void Execute(string? project, string? asset, bool json)
    {
        var dataDir = CommandHelper.GetDataDirectory();
        var projectsDir = CommandHelper.GetDefaultProjectsPath();
        var componentsDir = CommandHelper.GetComponentsDirectory();
        var dbPath = CommandHelper.GetDatabasePath();

        var projectMarkerService = new ProjectMarkerService();
        var assetJsonReader = new AssetJsonReader();
        var partJsonReader = new PartJsonReader();

        // プロジェクト情報を収集
        var projects = new List<ProjectInfo>();
        if (Directory.Exists(projectsDir))
        {
            foreach (var dir in Directory.GetDirectories(projectsDir))
            {
                if (!projectMarkerService.Exists(dir)) continue;
                var marker = projectMarkerService.Read(dir);
                if (marker == null) continue;

                // 特定プロジェクトが指定された場合はフィルタ
                if (!string.IsNullOrEmpty(project) &&
                    !marker.Name.Equals(project, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var projectInfo = new ProjectInfo
                {
                    Name = marker.Name,
                    Path = dir,
                    Id = marker.ProjectId,
                    RegisteredAt = marker.RegisteredAt
                };

                var assetsDir = Path.Combine(dir, "assets");
                if (Directory.Exists(assetsDir))
                {
                    foreach (var assetDir in Directory.GetDirectories(assetsDir))
                    {
                        if (!assetJsonReader.Exists(assetDir)) continue;
                        var assetJson = assetJsonReader.Read(assetDir);
                        if (assetJson == null) continue;

                        // 特定装置が指定された場合はフィルタ
                        if (!string.IsNullOrEmpty(asset) &&
                            !assetJson.Name.Equals(asset, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        projectInfo.Assets.Add(new AssetInfo
                        {
                            Name = assetJson.Name,
                            DisplayName = assetJson.DisplayName,
                            Id = assetJson.Id
                        });
                    }
                }

                projects.Add(projectInfo);
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

        // Qdrant 接続状態
        var qdrantStatus = CheckQdrantConnection();

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                system = new
                {
                    dataDirectory = dataDir,
                    database = dbPath,
                    databaseExists = File.Exists(dbPath),
                    qdrant = qdrantStatus
                },
                projects,
                sharedParts = parts,
                summary = new
                {
                    projectCount = projects.Count,
                    assetCount = projects.Sum(p => p.Assets.Count),
                    partCount = parts.Count
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
            Console.WriteLine($"  Qdrant: {qdrantStatus}");
            Console.WriteLine();

            // プロジェクト情報
            if (projects.Count > 0)
            {
                Console.WriteLine($"Projects: {projects.Count}");
                foreach (var proj in projects)
                {
                    Console.WriteLine($"  {proj.Name}");
                    Console.WriteLine($"    Path: {proj.Path}");
                    Console.WriteLine($"    ID: {proj.Id}");
                    Console.WriteLine($"    Assets: {proj.Assets.Count}");

                    if (!string.IsNullOrEmpty(project) || !string.IsNullOrEmpty(asset))
                    {
                        foreach (var assetInfo in proj.Assets)
                        {
                            Console.WriteLine($"      - {assetInfo.Name} ({assetInfo.DisplayName})");
                        }
                    }
                }
                Console.WriteLine();
            }
            else
            {
                Console.WriteLine("Projects: (none registered)");
                Console.WriteLine();
            }

            // 共有パーツ情報
            Console.WriteLine($"Shared Parts: {parts.Count}");
            if (parts.Count > 0 && string.IsNullOrEmpty(project))
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
            Console.WriteLine($"  Projects: {projects.Count}");
            Console.WriteLine($"  Assets: {projects.Sum(p => p.Assets.Count)}");
            Console.WriteLine($"  Shared Parts: {parts.Count}");
        }
    }

    private static string CheckQdrantConnection()
    {
        var host = Environment.GetEnvironmentVariable("DA_QDRANT_HOST") ?? "localhost";
        // Qdrant.Client は gRPC を使用するため、デフォルトは 6334
        var portStr = Environment.GetEnvironmentVariable("DA_QDRANT_GRPC_PORT")
                      ?? Environment.GetEnvironmentVariable("DA_QDRANT_PORT")
                      ?? "6334";
        var port = int.TryParse(portStr, out var p) ? p : 6334;
        var enabled = Environment.GetEnvironmentVariable("DA_QDRANT_ENABLED");

        if (enabled?.ToLowerInvariant() == "false")
        {
            return "Disabled";
        }

        try
        {
            var embeddingProvider = new MockEmbeddingProvider();
            using var qdrantService = new QdrantService(host, port, embeddingProvider);

            var connected = qdrantService.CheckConnectionAsync().GetAwaiter().GetResult();
            if (!connected)
            {
                return $"Not connected ({host}:{port})";
            }

            var collectionExists = qdrantService.CollectionExistsAsync().GetAwaiter().GetResult();
            if (!collectionExists)
            {
                return $"Connected ({host}:{port}) - Collection not created";
            }

            var pointCount = qdrantService.GetPointCountAsync().GetAwaiter().GetResult();
            return $"Connected ({host}:{port}) - {pointCount} points";
        }
        catch (Exception ex)
        {
            return $"Error ({host}:{port}): {ex.Message}";
        }
    }

    private class ProjectInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public Guid Id { get; set; }
        public DateTime RegisteredAt { get; set; }
        public List<AssetInfo> Assets { get; set; } = new();
    }

    private class AssetInfo
    {
        public string Name { get; set; } = string.Empty;
        public string? DisplayName { get; set; }
        public Guid Id { get; set; }
    }

    private class PartInfo
    {
        public string PartNumber { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public Guid Id { get; set; }
    }
}
