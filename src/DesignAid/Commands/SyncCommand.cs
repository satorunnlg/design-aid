using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
using System.Text.Json;
using DesignAid.Application.Services;
using DesignAid.Infrastructure.Embedding;
using DesignAid.Infrastructure.FileSystem;
using DesignAid.Infrastructure.Persistence;
using DesignAid.Infrastructure.VectorSearch;
using Microsoft.EntityFrameworkCore;

namespace DesignAid.Commands;

/// <summary>
/// ファイルシステムとDBを同期するコマンド。
/// </summary>
public class SyncCommand : Command
{
    public SyncCommand() : base("sync", "ファイルシステムとDBを同期")
    {
        this.Add(new Option<bool>("--dry-run", "変更確認のみ（実際の同期は行わない）"));
        this.Add(new Option<bool>("--include-vectors", "ベクトルインデックスへの同期も含む"));
        this.Add(new Option<bool>("--force", "強制同期（ハッシュを再計算）"));
        this.Add(new Option<bool>("--json", "JSON形式で出力"));

        this.Handler = CommandHandler.Create<bool, bool, bool, bool>(Execute);
    }

    private static void Execute(bool dryRun, bool includeVectors, bool force, bool json)
    {
        if (CommandHelper.EnsureDataDirectory() == null) return;
        var componentsDir = CommandHelper.GetComponentsDirectory();

        if (!Directory.Exists(componentsDir))
        {
            if (json)
            {
                Console.WriteLine(JsonSerializer.Serialize(new { success = true, changes = Array.Empty<object>() }));
            }
            else
            {
                Console.WriteLine("同期対象のパーツがありません");
            }
            return;
        }

        var hashService = new HashService();
        var partJsonReader = new PartJsonReader();
        var changes = new List<SyncChange>();

        if (!json)
        {
            Console.WriteLine("Syncing design data...");
            Console.WriteLine();
        }

        foreach (var partDir in Directory.GetDirectories(componentsDir))
        {
            var partNumber = Path.GetFileName(partDir);

            if (!partJsonReader.Exists(partDir))
            {
                // part.json がない場合は新規作成を提案
                changes.Add(new SyncChange
                {
                    PartNumber = partNumber,
                    Action = "SKIP",
                    Message = "part.json が存在しません（da part add で作成してください）"
                });
                continue;
            }

            var partJson = partJsonReader.Read(partDir);
            if (partJson == null)
            {
                changes.Add(new SyncChange
                {
                    PartNumber = partNumber,
                    Action = "ERROR",
                    Message = "part.json の読み込みに失敗しました"
                });
                continue;
            }

            // 現在のファイルをスキャン
            var currentFiles = Directory.GetFiles(partDir)
                .Where(f => !Path.GetFileName(f).Equals("part.json", StringComparison.OrdinalIgnoreCase))
                .Select(f => Path.GetRelativePath(partDir, f))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // 登録済みファイル
            var registeredFiles = partJson.Artifacts
                .Select(a => a.Path)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // 新規ファイルを検出
            var newFiles = currentFiles.Except(registeredFiles, StringComparer.OrdinalIgnoreCase).ToList();

            // 削除されたファイルを検出
            var deletedFiles = registeredFiles.Except(currentFiles, StringComparer.OrdinalIgnoreCase).ToList();

            // 変更されたファイルを検出
            var modifiedFiles = new List<string>();
            foreach (var artifact in partJson.Artifacts)
            {
                var filePath = Path.Combine(partDir, artifact.Path);
                if (!File.Exists(filePath)) continue;

                if (!string.IsNullOrEmpty(artifact.Hash))
                {
                    var currentHash = hashService.ComputeHash(filePath);
                    if (currentHash.Value != artifact.Hash.ToLowerInvariant())
                    {
                        modifiedFiles.Add(artifact.Path);
                    }
                }
            }

            if (newFiles.Count == 0 && deletedFiles.Count == 0 && modifiedFiles.Count == 0 && !force)
            {
                // 変更なし
                continue;
            }

            var change = new SyncChange
            {
                PartNumber = partNumber,
                NewFiles = newFiles,
                DeletedFiles = deletedFiles,
                ModifiedFiles = modifiedFiles
            };

            if (newFiles.Count > 0 || deletedFiles.Count > 0 || modifiedFiles.Count > 0)
            {
                change.Action = "UPDATE";
                change.Message = $"New: {newFiles.Count}, Modified: {modifiedFiles.Count}, Deleted: {deletedFiles.Count}";
            }
            else if (force)
            {
                change.Action = "REFRESH";
                change.Message = "強制更新";
            }

            // 実際の同期処理
            if (!dryRun && (change.Action == "UPDATE" || change.Action == "REFRESH"))
            {
                var newArtifacts = new List<ArtifactEntry>();

                foreach (var file in currentFiles)
                {
                    var filePath = Path.Combine(partDir, file);
                    var hash = hashService.ComputeHash(filePath);
                    newArtifacts.Add(new ArtifactEntry
                    {
                        Path = file,
                        Hash = hash.Value
                    });
                }

                var updatedPartJson = partJson with { Artifacts = newArtifacts };
                partJsonReader.Write(partDir, updatedPartJson);

                change.Synced = true;
            }

            changes.Add(change);
        }

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                success = true,
                dryRun,
                changes
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        else
        {
            if (changes.Count == 0)
            {
                Console.WriteLine("変更はありません");

                // ファイル変更がなくてもベクトルインデックス同期は実行する
                if (includeVectors && !dryRun)
                {
                    Console.WriteLine();
                    SyncVectorIndex(componentsDir, partJsonReader);
                }
                return;
            }

            foreach (var change in changes)
            {
                var prefix = change.Action switch
                {
                    "UPDATE" => "[UPDATE]",
                    "REFRESH" => "[REFRESH]",
                    "NEW" => "[NEW]",
                    "ERROR" => "[ERROR]",
                    "SKIP" => "[SKIP]",
                    _ => "[INFO]"
                };

                Console.WriteLine($"{prefix} {change.PartNumber}");
                Console.WriteLine($"    {change.Message}");

                if (change.NewFiles.Count > 0)
                {
                    Console.WriteLine($"    + New: {string.Join(", ", change.NewFiles)}");
                }
                if (change.ModifiedFiles.Count > 0)
                {
                    Console.WriteLine($"    ~ Modified: {string.Join(", ", change.ModifiedFiles)}");
                }
                if (change.DeletedFiles.Count > 0)
                {
                    Console.WriteLine($"    - Deleted: {string.Join(", ", change.DeletedFiles)}");
                }

                if (dryRun && (change.Action == "UPDATE" || change.Action == "REFRESH"))
                {
                    Console.WriteLine("    (dry-run: 実際の同期は行われません)");
                }
                else if (change.Synced)
                {
                    Console.WriteLine("    ✓ Synced");
                }

                Console.WriteLine();
            }

            var updateCount = changes.Count(c => c.Action == "UPDATE" || c.Action == "REFRESH");
            var errorCount = changes.Count(c => c.Action == "ERROR");

            Console.WriteLine($"Sync complete: {updateCount} updated, {errorCount} errors");

            if (dryRun)
            {
                Console.WriteLine("(dry-run モードのため、実際の変更は行われていません)");
            }

            if (includeVectors && !dryRun)
            {
                Console.WriteLine();
                SyncVectorIndex(componentsDir, partJsonReader);
            }
            else if (includeVectors && dryRun)
            {
                Console.WriteLine();
                Console.WriteLine("[INFO] dry-run モードのためベクトルインデックスへの同期はスキップされました");
            }
        }
    }

    private static void SyncVectorIndex(string componentsDir, PartJsonReader partJsonReader)
    {
        Console.WriteLine("Syncing to vector index...");

        try
        {
            var settings = CommandHelper.LoadSettings();
            if (!settings.GetBool("vector_search.enabled", true))
            {
                Console.WriteLine("[INFO] ベクトル検索が無効化されています");
                return;
            }

            var dimensions = settings.GetInt("embedding.dimensions", 384);
            var embeddingProvider = new MockEmbeddingProvider(dimensions);
            var dbPath = CommandHelper.GetDatabasePath();

            var optionsBuilder = new DbContextOptionsBuilder<DesignAidDbContext>();
            optionsBuilder.UseSqlite($"Data Source={dbPath}");
            using var context = new DesignAidDbContext(optionsBuilder.Options);

            // マイグレーションを適用
            context.Database.Migrate();

            var dataDir = CommandHelper.GetDataDirectory()!;
            var hnswIndexPath = Path.Combine(dataDir,
                settings.Get("vector_search.hnsw_index_path", "hnsw_index.bin")!);
            using var vectorService = new VectorSearchService(context, embeddingProvider, hnswIndexPath);

            // パーツ情報を収集してベクトル化
            var points = new List<DesignKnowledgePoint>();

            foreach (var partDir in Directory.GetDirectories(componentsDir))
            {
                if (!partJsonReader.Exists(partDir)) continue;
                var partJson = partJsonReader.Read(partDir);
                if (partJson == null) continue;

                var contentParts = new List<string>
                {
                    partJson.Name,
                    partJson.Type
                };

                if (!string.IsNullOrEmpty(partJson.Memo))
                    contentParts.Add(partJson.Memo);

                if (partJson.Metadata != null)
                {
                    foreach (var kv in partJson.Metadata)
                    {
                        contentParts.Add($"{kv.Key}:{kv.Value}");
                    }
                }

                points.Add(new DesignKnowledgePoint
                {
                    Id = partJson.Id,
                    PartId = partJson.Id,
                    PartNumber = partJson.PartNumber,
                    Type = "spec",
                    Content = string.Join(" ", contentParts),
                    CreatedAt = DateTime.UtcNow
                });
            }

            if (points.Count > 0)
            {
                vectorService.UpsertPartsAsync(points).GetAwaiter().GetResult();
                vectorService.RebuildIndexAsync().GetAwaiter().GetResult();
                Console.WriteLine($"[SUCCESS] {points.Count} 件のパーツをベクトルインデックスに同期しました");
            }
            else
            {
                Console.WriteLine("[INFO] 同期対象のパーツがありません");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] ベクトルインデックス同期中にエラーが発生しました: {ex.Message}");
        }
    }

    private class SyncChange
    {
        public string PartNumber { get; set; } = string.Empty;
        public string Action { get; set; } = "NONE";
        public string Message { get; set; } = string.Empty;
        public List<string> NewFiles { get; set; } = new();
        public List<string> DeletedFiles { get; set; } = new();
        public List<string> ModifiedFiles { get; set; } = new();
        public bool Synced { get; set; }
    }
}
