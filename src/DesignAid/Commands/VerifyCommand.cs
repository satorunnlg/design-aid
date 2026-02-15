using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
using System.Text.Json;
using DesignAid.Domain.Standards;
using DesignAid.Infrastructure.FileSystem;

namespace DesignAid.Commands;

/// <summary>
/// 設計基準に基づくバリデーションを実行するコマンド。
/// </summary>
public class VerifyCommand : Command
{
    public VerifyCommand() : base("verify", "設計基準に基づくバリデーション")
    {
        this.Add(new Option<string?>("--part", "特定パーツを検証"));
        this.Add(new Option<string?>("--standard", "特定の設計基準のみ検証"));
        this.Add(new Option<bool>("--json", "JSON形式で出力"));

        this.Handler = CommandHandler.Create<string?, string?, bool>(Execute);
    }

    private static void Execute(string? part, string? standard, bool json)
    {
        if (CommandHelper.EnsureDataDirectory() == null) return;
        var componentsDir = CommandHelper.GetComponentsDirectory();

        if (!Directory.Exists(componentsDir))
        {
            if (json)
            {
                Console.WriteLine(JsonSerializer.Serialize(new { success = true, results = Array.Empty<object>() }));
            }
            else
            {
                Console.WriteLine("検証対象のパーツがありません");
            }
            return;
        }

        // 設計基準を読み込む
        var standards = LoadStandards();

        // 特定の設計基準が指定された場合はフィルタ
        if (!string.IsNullOrEmpty(standard))
        {
            standards = standards.Where(s => s.StandardId.Equals(standard, StringComparison.OrdinalIgnoreCase)).ToList();
            if (standards.Count == 0)
            {
                Console.Error.WriteLine($"[ERROR] 設計基準が見つかりません: {standard}");
                Environment.ExitCode = 1;
                return;
            }
        }

        var partJsonReader = new PartJsonReader();
        var results = new List<VerifyResult>();
        var passCount = 0;
        var failCount = 0;

        if (!json)
        {
            Console.WriteLine("Verifying against design standards...");
            Console.WriteLine();
        }

        var targetDirs = Directory.GetDirectories(componentsDir);

        // 特定パーツが指定された場合
        if (!string.IsNullOrEmpty(part))
        {
            var specificDir = Path.Combine(componentsDir, part);
            if (!Directory.Exists(specificDir))
            {
                Console.Error.WriteLine($"[ERROR] パーツが見つかりません: {part}");
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

            var result = new VerifyResult
            {
                PartNumber = partJson.PartNumber,
                Details = new List<VerifyDetail>()
            };

            // 各設計基準で検証
            foreach (var std in standards)
            {
                // パーツに適用される基準かチェック
                if (partJson.Standards.Count > 0 &&
                    !partJson.Standards.Contains(std.StandardId))
                {
                    continue;
                }

                var validationResult = std.Validate(partJson);
                result.Details.Add(new VerifyDetail
                {
                    StandardId = std.StandardId,
                    StandardName = std.Name,
                    Status = validationResult.IsPass ? "PASS" : "FAIL",
                    Message = validationResult.Message,
                    Recommendation = validationResult.Recommendation
                });

                if (!validationResult.IsPass)
                {
                    result.HasFailure = true;
                }
            }

            // 結果を集計
            if (result.HasFailure)
            {
                result.Status = "FAIL";
                failCount++;
            }
            else
            {
                result.Status = "PASS";
                passCount++;
            }

            results.Add(result);

            if (!json)
            {
                PrintResult(result);
            }
        }

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                success = failCount == 0,
                summary = new { pass = passCount, fail = failCount },
                results
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        else
        {
            Console.WriteLine();
            Console.WriteLine($"Summary: {passCount} Pass, {failCount} Fail");
        }

        if (failCount > 0)
        {
            Environment.ExitCode = 1;
        }
    }

    private static List<IDesignStandard> LoadStandards()
    {
        // 組み込みの設計基準を返す
        return new List<IDesignStandard>
        {
            new MaterialStandard(),
            new ToleranceStandard()
        };
    }

    private static void PrintResult(VerifyResult result)
    {
        var prefix = result.Status == "PASS" ? "[PASS]" : "[FAIL]";
        Console.WriteLine($"{prefix} {result.PartNumber}");

        foreach (var detail in result.Details)
        {
            var detailPrefix = detail.Status == "PASS" ? "  ✓" : "  ✗";
            Console.WriteLine($"{detailPrefix} {detail.StandardId}: {detail.Message}");

            if (!string.IsNullOrEmpty(detail.Recommendation))
            {
                Console.WriteLine($"      Recommendation: {detail.Recommendation}");
            }
        }

        Console.WriteLine();
    }

    private class VerifyResult
    {
        public string PartNumber { get; set; } = string.Empty;
        public string Status { get; set; } = "PASS";
        public bool HasFailure { get; set; }
        public List<VerifyDetail> Details { get; set; } = new();
    }

    private class VerifyDetail
    {
        public string StandardId { get; set; } = string.Empty;
        public string StandardName { get; set; } = string.Empty;
        public string Status { get; set; } = "PASS";
        public string Message { get; set; } = string.Empty;
        public string? Recommendation { get; set; }
    }
}
