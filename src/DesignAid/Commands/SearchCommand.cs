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
/// 類似設計を検索するコマンド。
/// </summary>
public class SearchCommand : Command
{
    public SearchCommand() : base("search", "類似設計をベクトル検索")
    {
        this.Add(new Argument<string>("query", "検索クエリ"));
        this.Add(new Option<double>("--threshold", () => 0.7, "類似度閾値 (0.0-1.0)"));
        this.Add(new Option<int>("--top", () => 10, "上位N件を表示"));
        this.Add(new Option<bool>("--json", "JSON形式で出力"));
        this.Add(new Option<bool>("--local", "ローカル検索のみ使用（ベクトル検索を使用しない）"));

        this.Handler = CommandHandler.Create<string, double, int, bool, bool>(Execute);
    }

    private static async Task Execute(string query, double threshold, int top, bool json, bool local)
    {
        if (CommandHelper.EnsureDataDirectory() == null) return;

        // ベクトルインデックスの利用を試みる
        VectorSearchService? vectorService = null;
        bool useVector = false;

        if (!local)
        {
            try
            {
                var settings = CommandHelper.LoadSettings();
                if (settings.GetBool("vector_search.enabled", true))
                {
                    var dimensions = settings.GetInt("embedding.dimensions", 384);
                    var embeddingProvider = new MockEmbeddingProvider(dimensions);
                    var dbPath = CommandHelper.GetDatabasePath();

                    if (File.Exists(dbPath))
                    {
                        var optionsBuilder = new DbContextOptionsBuilder<DesignAidDbContext>();
                        optionsBuilder.UseSqlite($"Data Source={dbPath}");
                        var context = new DesignAidDbContext(optionsBuilder.Options);

                        var dataDir = CommandHelper.GetDataDirectory()!;
                        var hnswIndexPath = Path.Combine(dataDir,
                            settings.Get("vector_search.hnsw_index_path", "hnsw_index.bin")!);
                        vectorService = new VectorSearchService(context, embeddingProvider, hnswIndexPath);
                        useVector = await vectorService.IsAvailableAsync();
                    }
                }
            }
            catch
            {
                // ベクトル検索の初期化失敗時はローカル検索にフォールバック
            }
        }

        if (useVector && vectorService != null)
        {
            await ExecuteVectorSearch(query, threshold, top, json, vectorService);
            vectorService.Dispose();
        }
        else
        {
            ExecuteLocalSearch(query, threshold, top, json);
        }
    }

    private static async Task ExecuteVectorSearch(
        string query,
        double threshold,
        int top,
        bool json,
        VectorSearchService vectorService)
    {
        if (!json)
        {
            Console.WriteLine("Searching similar designs (Vector Search)...");
            Console.WriteLine();
            Console.WriteLine($"Query: \"{query}\"");
            Console.WriteLine();
        }

        var results = await vectorService.SearchAsync(query, threshold, top);

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                query,
                threshold,
                mode = "vector",
                results = results.Select(r => new
                {
                    score = r.Score,
                    partNumber = r.PartNumber,
                    content = r.Content,
                    projectName = r.ProjectName,
                    assetName = r.AssetName
                })
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        else
        {
            if (results.Count == 0)
            {
                Console.WriteLine("Results: (no matches found)");
                Console.WriteLine();
                Console.WriteLine("ヒント: `daid sync --include-vectors` でパーツをベクトルインデックスに同期してください");
                return;
            }

            Console.WriteLine("Results:");
            var rank = 1;
            foreach (var result in results)
            {
                var projectInfo = string.IsNullOrEmpty(result.ProjectName)
                    ? ""
                    : $" (Project: {result.ProjectName})";

                Console.WriteLine($"{rank}. [{result.Score:F2}] {result.PartNumber}{projectInfo}");

                if (!string.IsNullOrEmpty(result.Content))
                {
                    var content = result.Content.Length > 80
                        ? result.Content[..80] + "..."
                        : result.Content;
                    Console.WriteLine($"     \"{content}\"");
                }
                Console.WriteLine();
                rank++;
            }

            Console.WriteLine($"Found {results.Count} similar design(s)");
        }
    }

    private static void ExecuteLocalSearch(string query, double threshold, int top, bool json)
    {
        // ベクトル検索が利用できない場合は、ローカルキーワード検索を行う
        var componentsDir = CommandHelper.GetComponentsDirectory();

        if (!Directory.Exists(componentsDir))
        {
            if (json)
            {
                Console.WriteLine(JsonSerializer.Serialize(new { query, results = Array.Empty<object>() }));
            }
            else
            {
                Console.WriteLine("検索対象のパーツがありません");
            }
            return;
        }

        var partJsonReader = new PartJsonReader();
        var results = new List<LocalSearchResult>();

        // クエリをキーワードに分割
        var keywords = query.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (!json)
        {
            Console.WriteLine("Searching similar designs (Local Search)...");
            Console.WriteLine();
            Console.WriteLine($"Query: \"{query}\"");
            Console.WriteLine();
            Console.WriteLine("[INFO] ベクトルインデックス未構築のためキーワードベースの検索を使用しています");
            Console.WriteLine("       ベクトル検索を有効にするには `daid sync --include-vectors` を実行してください");
            Console.WriteLine();
        }

        foreach (var partDir in Directory.GetDirectories(componentsDir))
        {
            if (!partJsonReader.Exists(partDir)) continue;
            var partJson = partJsonReader.Read(partDir);
            if (partJson == null) continue;

            // スコアを計算（簡易的なキーワードマッチング）
            var score = CalculateScore(partJson, keywords);

            if (score >= threshold)
            {
                results.Add(new LocalSearchResult
                {
                    Score = score,
                    PartNumber = partJson.PartNumber,
                    Name = partJson.Name,
                    Type = partJson.Type,
                    Memo = partJson.Memo,
                    MatchedFields = GetMatchedFields(partJson, keywords)
                });
            }
        }

        // スコア降順でソート
        results = results.OrderByDescending(r => r.Score).Take(top).ToList();

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                query,
                threshold,
                mode = "local",
                results
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        else
        {
            if (results.Count == 0)
            {
                Console.WriteLine("Results: (no matches found)");
                return;
            }

            Console.WriteLine("Results:");
            var rank = 1;
            foreach (var result in results)
            {
                Console.WriteLine($"{rank}. [{result.Score:F2}] {result.PartNumber}");
                Console.WriteLine($"     Name: {result.Name}");
                Console.WriteLine($"     Type: {result.Type}");
                if (!string.IsNullOrEmpty(result.Memo))
                {
                    var memo = result.Memo.Length > 60
                        ? result.Memo[..60] + "..."
                        : result.Memo;
                    Console.WriteLine($"     Memo: \"{memo}\"");
                }
                if (result.MatchedFields.Count > 0)
                {
                    Console.WriteLine($"     Matched: {string.Join(", ", result.MatchedFields)}");
                }
                Console.WriteLine();
                rank++;
            }

            Console.WriteLine($"Found {results.Count} similar design(s)");
        }
    }

    private static double CalculateScore(PartJson partJson, string[] keywords)
    {
        if (keywords.Length == 0) return 0;

        var matchCount = 0;
        var totalWeight = 0.0;

        var searchableTexts = new Dictionary<string, double>
        {
            { partJson.PartNumber.ToLowerInvariant(), 1.0 },
            { partJson.Name.ToLowerInvariant(), 1.0 },
            { partJson.Type.ToLowerInvariant(), 0.5 },
            { partJson.Memo?.ToLowerInvariant() ?? "", 0.8 }
        };

        // メタデータも検索対象に追加
        if (partJson.Metadata != null)
        {
            foreach (var kv in partJson.Metadata)
            {
                searchableTexts[$"{kv.Key}: {kv.Value}".ToLowerInvariant()] = 0.6;
            }
        }

        foreach (var keyword in keywords)
        {
            var keywordMatched = false;
            foreach (var (text, weight) in searchableTexts)
            {
                if (text.Contains(keyword))
                {
                    totalWeight += weight;
                    keywordMatched = true;
                }
            }
            if (keywordMatched) matchCount++;
        }

        // スコア = (マッチしたキーワード数 / 全キーワード数) * 加重平均
        var keywordRatio = (double)matchCount / keywords.Length;
        var weightedScore = totalWeight / (keywords.Length * searchableTexts.Count);

        return (keywordRatio * 0.7) + (weightedScore * 0.3);
    }

    private static List<string> GetMatchedFields(PartJson partJson, string[] keywords)
    {
        var matched = new List<string>();

        foreach (var keyword in keywords)
        {
            if (partJson.PartNumber.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                matched.Add("part_number");
            if (partJson.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                matched.Add("name");
            if (partJson.Memo?.Contains(keyword, StringComparison.OrdinalIgnoreCase) == true)
                matched.Add("memo");
            if (partJson.Metadata?.Any(kv =>
                kv.Key.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                kv.Value?.ToString()?.Contains(keyword, StringComparison.OrdinalIgnoreCase) == true) == true)
                matched.Add("metadata");
        }

        return matched.Distinct().ToList();
    }

    private class LocalSearchResult
    {
        public double Score { get; set; }
        public string PartNumber { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string? Memo { get; set; }
        public List<string> MatchedFields { get; set; } = new();
    }
}
