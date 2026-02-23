using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DesignAid.Application.Services;
using DesignAid.Domain.Entities;
using DesignAid.Domain.ValueObjects;
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
        this.Add(new Option<bool>("--skip-db", "DB同期をスキップ"));

        this.Handler = CommandHandler.Create<bool, bool, bool, bool, bool>(Execute);
    }

    private static void Execute(bool dryRun, bool includeVectors, bool force, bool json, bool skipDb)
    {
        if (CommandHelper.EnsureDataDirectory() == null) return;
        var componentsDir = CommandHelper.GetComponentsDirectory();
        var assetsDir = CommandHelper.GetAssetsDirectory();

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

            // パーツがなくてもアセットの DB 同期は実行する
            if (!skipDb && !dryRun)
            {
                SyncToDatabase(assetsDir, null, new PartJsonReader());
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

                // DB 同期（ファイル変更がなくても実行）
                if (!skipDb && !dryRun)
                {
                    Console.WriteLine();
                    SyncToDatabase(assetsDir, componentsDir, partJsonReader);
                }

                // ファイル変更がなくてもベクトルインデックス同期は実行する
                if (includeVectors && !dryRun)
                {
                    Console.WriteLine();
                    SyncVectorIndex(componentsDir, assetsDir, partJsonReader);
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

            // DB 同期
            if (!skipDb && !dryRun)
            {
                Console.WriteLine();
                SyncToDatabase(assetsDir, componentsDir, partJsonReader);
            }

            if (includeVectors && !dryRun)
            {
                Console.WriteLine();
                SyncVectorIndex(componentsDir, assetsDir, partJsonReader);
            }
            else if (includeVectors && dryRun)
            {
                Console.WriteLine();
                Console.WriteLine("[INFO] dry-run モードのためベクトルインデックスへの同期はスキップされました");
            }
        }
    }

    /// <summary>
    /// ファイルシステムの asset.json / part.json / asset_links.json を DB に UPSERT する。
    /// </summary>
    private static void SyncToDatabase(string assetsDir, string? componentsDir, PartJsonReader partJsonReader)
    {
        Console.WriteLine("Syncing to database...");

        try
        {
            using var db = CommandHelper.CreateDbContext();
            var assetJsonReader = new AssetJsonReader();

            int assetCount = 0, partCount = 0, linkCount = 0;

            // 1. Assets の UPSERT
            if (Directory.Exists(assetsDir))
            {
                foreach (var assetDir in Directory.GetDirectories(assetsDir))
                {
                    if (!assetJsonReader.Exists(assetDir)) continue;
                    var assetJson = assetJsonReader.Read(assetDir);
                    if (assetJson == null) continue;

                    var dbAsset = db.Assets.FirstOrDefault(a => a.Name == assetJson.Name);
                    if (dbAsset == null)
                    {
                        // 新規登録
                        dbAsset = Domain.Entities.Asset.Create(assetJson.Name, assetDir, assetJson.DisplayName, assetJson.Description);
                        if (assetJson.Status != null && TryParseAssetStatus(assetJson.Status, out var status))
                        {
                            dbAsset.Update(status: status);
                        }
                        if (assetJson.Tags != null && assetJson.Tags.Count > 0)
                        {
                            dbAsset.Update(tags: assetJson.Tags);
                        }
                        db.Assets.Add(dbAsset);
                        assetCount++;
                    }
                    else
                    {
                        // 既存更新
                        AssetStatus? newStatus = null;
                        if (assetJson.Status != null && TryParseAssetStatus(assetJson.Status, out var parsed))
                        {
                            newStatus = parsed;
                        }
                        dbAsset.Update(
                            displayName: assetJson.DisplayName,
                            description: assetJson.Description,
                            status: newStatus,
                            tags: assetJson.Tags);
                        dbAsset.UpdatePath(assetDir);
                    }
                }
                db.SaveChanges();
            }

            // 2. Parts の UPSERT
            if (componentsDir != null && Directory.Exists(componentsDir))
            {
                foreach (var partDir in Directory.GetDirectories(componentsDir))
                {
                    if (!partJsonReader.Exists(partDir)) continue;
                    var partJson = partJsonReader.Read(partDir);
                    if (partJson == null) continue;

                    if (!PartNumber.TryCreate(partJson.PartNumber, out var pn)) continue;

                    var dbPart = db.Parts.FirstOrDefault(p => p.PartNumber == pn);
                    if (dbPart == null)
                    {
                        // 新規登録（TPH に従い型別に生成）
                        var partType = PartJsonReader.ParsePartType(partJson);
                        DesignComponent newPart = partType switch
                        {
                            PartType.Purchased => PurchasedPart.Create(pn, partJson.Name, partDir),
                            PartType.Standard => StandardPart.Create(pn, partJson.Name, partDir),
                            _ => FabricatedPart.Create(pn, partJson.Name, partDir)
                        };

                        if (newPart is PurchasedPart pp && partJson.UnitPrice != null)
                        {
                            pp.UpdatePurchaseInfo(unitPrice: partJson.UnitPrice, currency: partJson.Currency ?? "JPY");
                        }
                        if (partJson.Memo != null) newPart.Memo = partJson.Memo;

                        db.Parts.Add(newPart);
                        partCount++;
                    }
                    else
                    {
                        // 既存更新
                        dbPart.UpdateName(partJson.Name);
                        if (partJson.Memo != null) dbPart.Memo = partJson.Memo;
                        if (dbPart is PurchasedPart existingPp && partJson.UnitPrice != null)
                        {
                            existingPp.UpdatePurchaseInfo(unitPrice: partJson.UnitPrice, currency: partJson.Currency);
                        }
                    }
                }
                db.SaveChanges();
            }

            // 3. Asset Links（AssetComponents + AssetSubAssets）の UPSERT
            if (Directory.Exists(assetsDir))
            {
                var jsonOptions = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
                };

                foreach (var assetDir in Directory.GetDirectories(assetsDir))
                {
                    var linksPath = Path.Combine(assetDir, "asset_links.json");
                    if (!File.Exists(linksPath)) continue;

                    var assetName = Path.GetFileName(assetDir);
                    var dbAsset = db.Assets.FirstOrDefault(a => a.Name == assetName);
                    if (dbAsset == null) continue;

                    AssetLinksJson? links;
                    try
                    {
                        var linksJson = File.ReadAllText(linksPath);
                        links = JsonSerializer.Deserialize<AssetLinksJson>(linksJson, jsonOptions);
                    }
                    catch (JsonException)
                    {
                        continue;
                    }
                    if (links == null) continue;

                    // パーツリンクの UPSERT
                    if (links.Parts != null)
                    {
                        foreach (var partLink in links.Parts)
                        {
                            if (!PartNumber.TryCreate(partLink.PartNumber, out var pn)) continue;
                            var dbPart = db.Parts.FirstOrDefault(p => p.PartNumber == pn);
                            if (dbPart == null) continue;

                            var existing = db.AssetComponents
                                .FirstOrDefault(c => c.AssetId == dbAsset.Id && c.PartId == dbPart.Id);
                            if (existing != null)
                            {
                                existing.UpdateQuantity(partLink.Quantity);
                            }
                            else
                            {
                                db.AssetComponents.Add(
                                    AssetComponent.Create(dbAsset.Id, dbPart.Id, partLink.Quantity));
                                linkCount++;
                            }
                        }
                    }

                    // 子装置リンクの UPSERT
                    if (links.ChildAssets != null)
                    {
                        foreach (var childLink in links.ChildAssets)
                        {
                            var dbChild = db.Assets.FirstOrDefault(a => a.Name == childLink.ChildAssetName);
                            if (dbChild == null) continue;

                            var existing = db.AssetSubAssets
                                .FirstOrDefault(s => s.ParentAssetId == dbAsset.Id && s.ChildAssetId == dbChild.Id);
                            if (existing != null)
                            {
                                existing.Quantity = childLink.Quantity;
                                existing.Notes = childLink.Notes;
                            }
                            else
                            {
                                db.AssetSubAssets.Add(new AssetSubAsset
                                {
                                    ParentAssetId = dbAsset.Id,
                                    ChildAssetId = dbChild.Id,
                                    Quantity = childLink.Quantity,
                                    Notes = childLink.Notes,
                                    CreatedAt = DateTime.UtcNow
                                });
                                linkCount++;
                            }
                        }
                    }
                }
                db.SaveChanges();
            }

            Console.WriteLine($"[SUCCESS] DB同期完了: {assetCount} 件の装置、{partCount} 件のパーツを新規登録、{linkCount} 件のリンクを追加");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ERROR] DB同期中にエラーが発生しました: {ex.Message}");
        }
    }

    /// <summary>
    /// ステータス文字列を AssetStatus に変換する。
    /// </summary>
    private static bool TryParseAssetStatus(string status, out AssetStatus result)
    {
        result = status.ToLowerInvariant() switch
        {
            "active" => AssetStatus.Active,
            "dormant" => AssetStatus.Dormant,
            "archived" => AssetStatus.Archived,
            "third_party" => AssetStatus.ThirdParty,
            _ => AssetStatus.Active
        };
        return status.ToLowerInvariant() is "active" or "dormant" or "archived" or "third_party";
    }

    private static void SyncVectorIndex(string componentsDir, string assetsDir, PartJsonReader partJsonReader)
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

            var embeddingProvider = EmbeddingProviderFactory.Create(settings);

            // CreateDbContext() で統一（Phase 5）
            using var context = CommandHelper.CreateDbContext();

            var dataDir = CommandHelper.GetDataDirectory()!;
            var hnswIndexPath = Path.Combine(dataDir,
                settings.Get("vector_search.hnsw_index_path", "hnsw_index.bin")!);
            using var vectorService = new VectorSearchService(context, embeddingProvider, hnswIndexPath);

            // パーツ情報を収集してベクトル化
            var points = new List<DesignKnowledgePoint>();

            if (Directory.Exists(componentsDir))
            {
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
            }

            // asset 文書（.md ファイル）を収集してベクトル化
            var docPoints = CollectAssetDocPoints(assetsDir);

            // 現在 assets/ に存在するアセットの asset_doc エントリのみ削除（アーカイブ済みは保持）
            var activeAssetNames = docPoints.Select(d => d.AssetName).Where(n => n != null).Distinct().ToHashSet();
            var existingDocs = context.VectorIndex
                .Where(v => v.Type == "asset_doc" && v.AssetName != null && activeAssetNames.Contains(v.AssetName))
                .ToList();
            if (existingDocs.Count > 0)
            {
                context.VectorIndex.RemoveRange(existingDocs);
                context.SaveChanges();
            }

            // パーツとドキュメントを結合
            points.AddRange(docPoints);

            if (points.Count > 0)
            {
                vectorService.UpsertPartsAsync(points).GetAwaiter().GetResult();
                vectorService.RebuildIndexAsync().GetAwaiter().GetResult();

                var partCount = points.Count - docPoints.Count;
                var docCount = docPoints.Count;
                var messages = new List<string>();
                if (partCount > 0) messages.Add($"{partCount} 件のパーツ");
                if (docCount > 0) messages.Add($"{docCount} 件の文書チャンク");
                Console.WriteLine($"[SUCCESS] {string.Join("、", messages)}をベクトルインデックスに同期しました");
            }
            else
            {
                Console.WriteLine("[INFO] 同期対象のデータがありません");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] ベクトルインデックス同期中にエラーが発生しました: {ex.Message}");
        }
    }

    /// <summary>
    /// assets/ 以下の .md ファイルをチャンク分割して DesignKnowledgePoint リストを生成する。
    /// </summary>
    private static List<DesignKnowledgePoint> CollectAssetDocPoints(string assetsDir)
    {
        var docPoints = new List<DesignKnowledgePoint>();

        if (!Directory.Exists(assetsDir)) return docPoints;

        var assetJsonReader = new AssetJsonReader();

        foreach (var assetDir in Directory.GetDirectories(assetsDir))
        {
            if (!assetJsonReader.Exists(assetDir)) continue;
            var assetJson = assetJsonReader.Read(assetDir);
            if (assetJson == null) continue;

            var assetName = assetJson.Name;

            // .md ファイルを収集
            var mdFiles = Directory.GetFiles(assetDir, "*.md", SearchOption.TopDirectoryOnly);
            foreach (var mdFile in mdFiles)
            {
                var fileName = Path.GetFileName(mdFile);
                var content = File.ReadAllText(mdFile, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(content)) continue;

                var relativePath = $"assets/{assetName}/{fileName}";
                var chunks = ChunkByParagraph(content, 1000);

                for (int i = 0; i < chunks.Count; i++)
                {
                    // 決定論的 GUID: asset名 + ファイルパス + チャンクインデックスから生成
                    var deterministicId = GenerateDeterministicGuid($"{assetName}/{fileName}#{i}");

                    docPoints.Add(new DesignKnowledgePoint
                    {
                        Id = deterministicId,
                        PartId = deterministicId,
                        PartNumber = $"[DOC] {assetName}/{fileName}",
                        AssetName = assetName,
                        AssetId = assetJson.Id,
                        Type = "asset_doc",
                        Content = chunks[i],
                        FilePath = relativePath,
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }
        }

        return docPoints;
    }

    /// <summary>
    /// テキストを段落境界（空行）で分割し、最大 maxChars 文字のチャンクにまとめる。
    /// </summary>
    private static List<string> ChunkByParagraph(string text, int maxChars)
    {
        var chunks = new List<string>();
        // 空行で段落を分割
        var paragraphs = text.Split(new[] { "\r\n\r\n", "\n\n" }, StringSplitOptions.RemoveEmptyEntries);

        var currentChunk = new StringBuilder();
        foreach (var paragraph in paragraphs)
        {
            var trimmed = paragraph.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) continue;

            // 単一段落が maxChars を超える場合はそのまま1チャンクとして追加
            if (trimmed.Length > maxChars)
            {
                if (currentChunk.Length > 0)
                {
                    chunks.Add(currentChunk.ToString().Trim());
                    currentChunk.Clear();
                }
                chunks.Add(trimmed);
                continue;
            }

            // 現在のチャンクに追加すると maxChars を超える場合
            if (currentChunk.Length + trimmed.Length + 2 > maxChars && currentChunk.Length > 0)
            {
                chunks.Add(currentChunk.ToString().Trim());
                currentChunk.Clear();
            }

            if (currentChunk.Length > 0) currentChunk.Append("\n\n");
            currentChunk.Append(trimmed);
        }

        if (currentChunk.Length > 0)
        {
            chunks.Add(currentChunk.ToString().Trim());
        }

        return chunks;
    }

    /// <summary>
    /// 文字列から決定論的な GUID を生成する（SHA256 ベース）。
    /// </summary>
    private static Guid GenerateDeterministicGuid(string input)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        var guidBytes = new byte[16];
        Array.Copy(hash, guidBytes, 16);
        return new Guid(guidBytes);
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

    // asset_links.json のデータクラス
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
