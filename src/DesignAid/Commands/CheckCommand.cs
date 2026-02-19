using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
using System.Text.Json;
using DesignAid.Application.Services;
using DesignAid.Domain.ValueObjects;
using DesignAid.Infrastructure.FileSystem;

namespace DesignAid.Commands;

/// <summary>
/// ハッシュ整合性をチェックするコマンド。
/// </summary>
public class CheckCommand : Command
{
    public CheckCommand() : base("check", "ファイルハッシュの整合性を検証")
    {
        this.Add(new Option<string?>("--path", "チェック対象のパス（省略時はカレントプロジェクト）"));
        this.Add(new Option<bool>("--json", "JSON形式で出力"));
        this.Add(new Option<bool>("--verbose", "詳細出力"));
        this.Add(new Option<bool>("--duplicates", "パーツの重複・不整合を検出"));

        this.Handler = CommandHandler.Create<string?, bool, bool, bool>(Execute);
    }

    private static void Execute(string? path, bool json, bool verbose, bool duplicates)
    {
        if (path == null && CommandHelper.EnsureDataDirectory() == null) return;

        if (duplicates)
        {
            ExecuteDuplicateCheck(path, json);
            return;
        }

        var componentsDir = path ?? CommandHelper.GetComponentsDirectory();

        if (!Directory.Exists(componentsDir))
        {
            if (json)
            {
                Console.WriteLine(JsonSerializer.Serialize(new { success = true, results = Array.Empty<object>() }));
            }
            else
            {
                Console.WriteLine("チェック対象のパーツがありません");
            }
            return;
        }

        var hashService = new HashService();
        var partJsonReader = new PartJsonReader();
        var results = new List<CheckResult>();
        var okCount = 0;
        var warningCount = 0;
        var errorCount = 0;

        if (!json)
        {
            Console.WriteLine("Checking design integrity...");
            Console.WriteLine();
        }

        foreach (var partDir in Directory.GetDirectories(componentsDir))
        {
            if (!partJsonReader.Exists(partDir)) continue;
            var partJson = partJsonReader.Read(partDir);
            if (partJson == null) continue;

            var result = new CheckResult
            {
                PartNumber = partJson.PartNumber,
                DirectoryPath = partDir,
                Details = new List<CheckDetail>()
            };

            // 成果物のハッシュを検証
            foreach (var artifact in partJson.Artifacts)
            {
                var filePath = Path.Combine(partDir, artifact.Path);
                var detail = new CheckDetail { File = artifact.Path };

                if (!File.Exists(filePath))
                {
                    detail.Status = "ERROR";
                    detail.Message = "ファイルが見つかりません";
                    result.HasError = true;
                }
                else
                {
                    try
                    {
                        var actualHash = hashService.ComputeHash(filePath);
                        if (FileHash.IsValid(artifact.Hash))
                        {
                            var expectedHash = new FileHash(artifact.Hash);
                            if (actualHash == expectedHash)
                            {
                                detail.Status = "OK";
                                detail.Message = "ハッシュ一致";
                            }
                            else
                            {
                                detail.Status = "WARNING";
                                detail.Message = "ハッシュ不整合（ファイルが変更されています）";
                                detail.Expected = artifact.Hash;
                                detail.Actual = actualHash.Value;
                                result.HasWarning = true;
                            }
                        }
                        else
                        {
                            detail.Status = "OK";
                            detail.Message = "ハッシュ未登録（新規追加）";
                            detail.Actual = actualHash.Value;
                        }
                    }
                    catch (Exception ex)
                    {
                        detail.Status = "ERROR";
                        detail.Message = $"ハッシュ計算エラー: {ex.Message}";
                        result.HasError = true;
                    }
                }

                result.Details.Add(detail);
            }

            // 未登録のファイルをチェック
            var registeredFiles = partJson.Artifacts.Select(a => a.Path.ToLowerInvariant()).ToHashSet();
            foreach (var file in Directory.GetFiles(partDir))
            {
                var fileName = Path.GetFileName(file);
                if (fileName.Equals("part.json", StringComparison.OrdinalIgnoreCase)) continue;

                var relativePath = Path.GetRelativePath(partDir, file);
                if (!registeredFiles.Contains(relativePath.ToLowerInvariant()))
                {
                    result.Details.Add(new CheckDetail
                    {
                        File = relativePath,
                        Status = "INFO",
                        Message = "未登録のファイル（syncで登録できます）"
                    });
                }
            }

            // 結果を集計
            if (result.HasError)
            {
                result.Status = "ERROR";
                errorCount++;
            }
            else if (result.HasWarning)
            {
                result.Status = "WARNING";
                warningCount++;
            }
            else
            {
                result.Status = "OK";
                okCount++;
            }

            results.Add(result);

            if (!json)
            {
                PrintResult(result, verbose);
            }
        }

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                success = errorCount == 0,
                summary = new { ok = okCount, warning = warningCount, error = errorCount },
                results
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        else
        {
            Console.WriteLine();
            Console.WriteLine($"Summary: {okCount} OK, {warningCount} Warning, {errorCount} Error");
        }

        if (errorCount > 0)
        {
            Environment.ExitCode = 5; // 整合性エラー
        }
    }

    private static void PrintResult(CheckResult result, bool verbose)
    {
        var prefix = result.Status switch
        {
            "OK" => "[OK]",
            "WARNING" => "[WARNING]",
            "ERROR" => "[ERROR]",
            _ => "[UNKNOWN]"
        };

        Console.WriteLine($"{prefix} {result.PartNumber}");

        foreach (var detail in result.Details)
        {
            if (!verbose && detail.Status == "OK") continue;

            var detailPrefix = detail.Status switch
            {
                "OK" => "  ✓",
                "WARNING" => "  ⚠",
                "ERROR" => "  ✗",
                "INFO" => "  ℹ",
                _ => "  -"
            };

            Console.WriteLine($"{detailPrefix} {detail.File}: {detail.Message}");

            if (verbose && detail.Expected != null)
            {
                Console.WriteLine($"      Expected: {detail.Expected}");
                Console.WriteLine($"      Actual:   {detail.Actual}");
            }
        }

        Console.WriteLine();
    }

    /// <summary>
    /// パーツの重複・不整合をチェックする。
    /// </summary>
    private static void ExecuteDuplicateCheck(string? path, bool json)
    {
        var componentsDir = path ?? CommandHelper.GetComponentsDirectory();

        if (!Directory.Exists(componentsDir))
        {
            if (json)
            {
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    success = true,
                    duplicates = Array.Empty<object>(),
                    orphans = Array.Empty<object>(),
                    mismatches = Array.Empty<object>()
                }));
            }
            else
            {
                Console.WriteLine("チェック対象のパーツがありません");
            }
            return;
        }

        var partJsonReader = new PartJsonReader();
        var duplicates = new List<DuplicateEntry>();
        var orphans = new List<string>();
        var mismatches = new List<MismatchEntry>();

        // パーツ番号→ディレクトリパスのマッピングを構築
        var partNumberMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var partDir in Directory.GetDirectories(componentsDir))
        {
            var dirName = Path.GetFileName(partDir);

            if (!partJsonReader.Exists(partDir))
            {
                // 孤立ディレクトリ: part.json がない
                orphans.Add(dirName);
                continue;
            }

            var partJson = partJsonReader.Read(partDir);
            if (partJson == null)
            {
                orphans.Add(dirName);
                continue;
            }

            // ディレクトリ名と part_number の不整合を検出
            if (!dirName.Equals(partJson.PartNumber, StringComparison.OrdinalIgnoreCase))
            {
                mismatches.Add(new MismatchEntry
                {
                    DirectoryName = dirName,
                    PartNumber = partJson.PartNumber
                });
            }

            // 重複検出用マップに追加
            if (!partNumberMap.TryGetValue(partJson.PartNumber, out var dirs))
            {
                dirs = new List<string>();
                partNumberMap[partJson.PartNumber] = dirs;
            }
            dirs.Add(dirName);
        }

        // 重複パーツ番号を抽出
        foreach (var (partNumber, dirs) in partNumberMap)
        {
            if (dirs.Count > 1)
            {
                duplicates.Add(new DuplicateEntry
                {
                    PartNumber = partNumber,
                    Directories = dirs
                });
            }
        }

        var hasIssues = duplicates.Count > 0 || orphans.Count > 0 || mismatches.Count > 0;

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                success = !hasIssues,
                duplicates,
                orphans,
                mismatches
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        else
        {
            Console.WriteLine("Checking for duplicates and inconsistencies...");
            Console.WriteLine();

            if (duplicates.Count > 0)
            {
                Console.WriteLine("[DUPLICATES] 重複パーツ番号:");
                foreach (var dup in duplicates)
                {
                    Console.WriteLine($"  ⚠ {dup.PartNumber}");
                    foreach (var dir in dup.Directories)
                    {
                        Console.WriteLine($"      - {dir}/");
                    }
                }
                Console.WriteLine();
            }

            if (orphans.Count > 0)
            {
                Console.WriteLine("[ORPHANS] part.json のないディレクトリ:");
                foreach (var orphan in orphans)
                {
                    Console.WriteLine($"  ⚠ {orphan}/");
                }
                Console.WriteLine();
            }

            if (mismatches.Count > 0)
            {
                Console.WriteLine("[MISMATCH] ディレクトリ名と part_number の不一致:");
                foreach (var mm in mismatches)
                {
                    Console.WriteLine($"  ⚠ ディレクトリ: {mm.DirectoryName}/ → part_number: {mm.PartNumber}");
                }
                Console.WriteLine();
            }

            if (!hasIssues)
            {
                Console.WriteLine("問題は見つかりませんでした");
            }
            else
            {
                Console.WriteLine($"Summary: {duplicates.Count} duplicate(s), {orphans.Count} orphan(s), {mismatches.Count} mismatch(es)");
            }
        }

        if (hasIssues)
        {
            Environment.ExitCode = 5;
        }
    }

    private class DuplicateEntry
    {
        public string PartNumber { get; set; } = string.Empty;
        public List<string> Directories { get; set; } = new();
    }

    private class MismatchEntry
    {
        public string DirectoryName { get; set; } = string.Empty;
        public string PartNumber { get; set; } = string.Empty;
    }

    private class CheckResult
    {
        public string PartNumber { get; set; } = string.Empty;
        public string DirectoryPath { get; set; } = string.Empty;
        public string Status { get; set; } = "OK";
        public bool HasWarning { get; set; }
        public bool HasError { get; set; }
        public List<CheckDetail> Details { get; set; } = new();
    }

    private class CheckDetail
    {
        public string File { get; set; } = string.Empty;
        public string Status { get; set; } = "OK";
        public string Message { get; set; } = string.Empty;
        public string? Expected { get; set; }
        public string? Actual { get; set; }
    }
}
