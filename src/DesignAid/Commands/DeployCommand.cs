using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
using System.Text.Json;
using DesignAid.Infrastructure.FileSystem;

namespace DesignAid.Commands;

/// <summary>
/// 手配パッケージを作成するコマンド。
/// </summary>
public class DeployCommand : Command
{
    public DeployCommand() : base("deploy", "手配パッケージを作成")
    {
        this.Add(new Option<string?>("--part", "特定パーツのみ"));
        this.Add(new Option<string?>("--output", "出力先ディレクトリ"));
        this.Add(new Option<bool>("--json", "JSON形式で出力"));
        this.Add(new Option<bool>("--no-confirm", "確認なしで実行"));
        this.Add(new Option<bool>("--dry-run", "実行せずにプレビューのみ"));

        this.Handler = CommandHandler.Create<string?, string?, bool, bool, bool>(Execute);
    }

    private static void Execute(string? part, string? output, bool json, bool noConfirm, bool dryRun)
    {
        var componentsDir = CommandHelper.GetComponentsDirectory();

        if (!Directory.Exists(componentsDir))
        {
            if (json)
            {
                Console.WriteLine(JsonSerializer.Serialize(new { success = false, error = "パーツが存在しません" }));
            }
            else
            {
                Console.Error.WriteLine("[ERROR] デプロイ対象のパーツがありません");
            }
            Environment.ExitCode = 1;
            return;
        }

        var partJsonReader = new PartJsonReader();
        var deployTargets = new List<DeployTarget>();

        // 対象パーツを収集
        var targetDirs = Directory.GetDirectories(componentsDir);
        if (!string.IsNullOrEmpty(part))
        {
            var specificDir = Path.Combine(componentsDir, part);
            if (!Directory.Exists(specificDir))
            {
                if (json)
                {
                    Console.WriteLine(JsonSerializer.Serialize(new { success = false, error = $"パーツが見つかりません: {part}" }));
                }
                else
                {
                    Console.Error.WriteLine($"[ERROR] パーツが見つかりません: {part}");
                }
                Environment.ExitCode = 1;
                return;
            }
            targetDirs = new[] { specificDir };
        }

        foreach (var partDir in targetDirs)
        {
            if (!partJsonReader.Exists(partDir)) continue;
            var partJson = partJsonReader.Read(partDir);
            if (partJson == null) continue;

            var files = Directory.GetFiles(partDir)
                .Where(f => !Path.GetFileName(f).Equals("part.json", StringComparison.OrdinalIgnoreCase))
                .Select(f => Path.GetFileName(f))
                .ToList();

            if (files.Count == 0) continue;

            deployTargets.Add(new DeployTarget
            {
                PartNumber = partJson.PartNumber,
                Name = partJson.Name,
                Type = partJson.Type,
                SourcePath = partDir,
                Files = files
            });
        }

        if (deployTargets.Count == 0)
        {
            if (json)
            {
                Console.WriteLine(JsonSerializer.Serialize(new { success = true, message = "デプロイ対象がありません" }));
            }
            else
            {
                Console.WriteLine("デプロイ対象のパーツがありません");
            }
            return;
        }

        // 出力先を決定
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
        var deployDir = output ?? Path.Combine(Directory.GetCurrentDirectory(), $"deploy_{timestamp}");

        if (!json)
        {
            if (dryRun)
            {
                Console.WriteLine("Deploy Preview (--dry-run)");
            }
            else
            {
                Console.WriteLine("Creating deployment package...");
            }
            Console.WriteLine();
            Console.WriteLine("Parts to deploy:");
            foreach (var target in deployTargets)
            {
                Console.WriteLine($"  - {target.PartNumber} ({target.Name})");
                Console.WriteLine($"      Type: {target.Type}");
                Console.WriteLine($"      Files: {string.Join(", ", target.Files)}");
            }
            Console.WriteLine();
            Console.WriteLine($"Output: {deployDir}");
            Console.WriteLine();

            if (dryRun)
            {
                Console.WriteLine($"Total: {deployTargets.Count} part(s) would be deployed");
                Console.WriteLine();
                Console.WriteLine("[DRY-RUN] 実際のデプロイは行われません");
                return;
            }

            if (!noConfirm)
            {
                Console.Write("Deploy these parts? [y/N] ");
                var response = Console.ReadLine()?.Trim().ToLowerInvariant();
                if (response != "y" && response != "yes")
                {
                    Console.WriteLine("キャンセルしました");
                    return;
                }
                Console.WriteLine();
            }
        }

        // ドライランの場合は JSON 出力も別処理
        if (dryRun && json)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                dryRun = true,
                wouldDeploy = deployTargets.Select(t => new
                {
                    partNumber = t.PartNumber,
                    name = t.Name,
                    type = t.Type,
                    files = t.Files
                }).ToList(),
                outputPath = deployDir
            }, new JsonSerializerOptions { WriteIndented = true }));
            return;
        }

        // デプロイ実行
        Directory.CreateDirectory(deployDir);

        var manifest = new DeployManifest
        {
            CreatedAt = DateTime.UtcNow,
            Parts = new List<DeployManifestPart>()
        };

        foreach (var target in deployTargets)
        {
            var targetDir = Path.Combine(deployDir, target.PartNumber);
            Directory.CreateDirectory(targetDir);

            var manifestPart = new DeployManifestPart
            {
                PartNumber = target.PartNumber,
                Name = target.Name,
                Type = target.Type,
                Files = new List<string>()
            };

            // ファイルをコピー
            foreach (var file in target.Files)
            {
                var srcPath = Path.Combine(target.SourcePath, file);
                var dstPath = Path.Combine(targetDir, file);
                File.Copy(srcPath, dstPath, overwrite: true);
                manifestPart.Files.Add(file);
            }

            // part_info.txt を作成
            var infoPath = Path.Combine(targetDir, "part_info.txt");
            var infoContent = $"""
                Part Number: {target.PartNumber}
                Name: {target.Name}
                Type: {target.Type}
                Deployed: {DateTime.Now:yyyy-MM-dd HH:mm:ss}

                Files:
                {string.Join(Environment.NewLine, target.Files.Select(f => $"  - {f}"))}
                """;
            File.WriteAllText(infoPath, infoContent);

            manifest.Parts.Add(manifestPart);

            if (!json)
            {
                Console.WriteLine($"[OK] {target.PartNumber} -> {targetDir}");
            }
        }

        // manifest.json を作成
        var manifestPath = Path.Combine(deployDir, "manifest.json");
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                success = true,
                outputPath = deployDir,
                manifest
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        else
        {
            Console.WriteLine();
            Console.WriteLine($"Deployment complete: {deployDir}");
            Console.WriteLine($"Total: {deployTargets.Count} part(s)");
            Console.WriteLine();
            Console.WriteLine("Directory structure:");
            Console.WriteLine($"  {Path.GetFileName(deployDir)}/");
            foreach (var target in deployTargets)
            {
                Console.WriteLine($"    {target.PartNumber}/");
                foreach (var file in target.Files)
                {
                    Console.WriteLine($"      {file}");
                }
                Console.WriteLine("      part_info.txt");
            }
            Console.WriteLine("    manifest.json");
        }
    }

    private class DeployTarget
    {
        public string PartNumber { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string SourcePath { get; set; } = string.Empty;
        public List<string> Files { get; set; } = new();
    }

    private class DeployManifest
    {
        public DateTime CreatedAt { get; set; }
        public List<DeployManifestPart> Parts { get; set; } = new();
    }

    private class DeployManifestPart
    {
        public string PartNumber { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public List<string> Files { get; set; } = new();
    }
}
