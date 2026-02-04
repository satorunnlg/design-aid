using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using DesignAid.Domain.Entities;
using DesignAid.Domain.ValueObjects;
using DesignAid.Infrastructure.Persistence;

namespace DesignAid.Application.Services;

/// <summary>
/// 手配パッケージの作成と管理を行うサービス。
/// </summary>
public class DeployService
{
    private readonly DesignAidDbContext _context;

    /// <summary>
    /// DeployService を初期化する。
    /// </summary>
    public DeployService(DesignAidDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// 手配対象のパーツを取得する。
    /// </summary>
    /// <param name="partNumbers">特定のパーツ番号（null の場合は全対象）</param>
    /// <param name="ct">キャンセルトークン</param>
    /// <returns>手配対象パーツリスト</returns>
    public async Task<List<DeployCandidate>> GetDeployCandidatesAsync(
        IEnumerable<string>? partNumbers = null,
        CancellationToken ct = default)
    {
        var query = _context.Parts
            .Include(p => p.HandoverRecords)
            .AsQueryable();

        if (partNumbers != null && partNumbers.Any())
        {
            var pnList = partNumbers.Select(pn => new PartNumber(pn)).ToList();
            query = query.Where(p => pnList.Contains(p.PartNumber));
        }

        var parts = await query.ToListAsync(ct);

        return parts
            .Where(p => ShouldDeploy(p))
            .Select(p => new DeployCandidate
            {
                Part = p,
                Reason = GetDeployReason(p),
                LastOrderedHash = GetLastOrderedHash(p)
            })
            .ToList();
    }

    /// <summary>
    /// 手配パッケージを作成する。
    /// </summary>
    /// <param name="outputPath">出力先ディレクトリ</param>
    /// <param name="partNumbers">特定のパーツ番号（null の場合は全対象）</param>
    /// <param name="markAsOrdered">手配済みとしてマークするか</param>
    /// <param name="ct">キャンセルトークン</param>
    /// <returns>作成結果</returns>
    public async Task<DeployResult> CreatePackageAsync(
        string outputPath,
        IEnumerable<string>? partNumbers = null,
        bool markAsOrdered = false,
        CancellationToken ct = default)
    {
        var result = new DeployResult { OutputPath = outputPath };

        var candidates = await GetDeployCandidatesAsync(partNumbers, ct);
        if (candidates.Count == 0)
        {
            return result;
        }

        // 出力ディレクトリを作成
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var packageDir = Path.Combine(outputPath, $"deploy_{timestamp}");
        Directory.CreateDirectory(packageDir);

        var manifest = new DeployManifest
        {
            CreatedAt = DateTime.UtcNow,
            Parts = new List<DeployManifestPart>()
        };

        foreach (var candidate in candidates)
        {
            try
            {
                var partDir = Path.Combine(packageDir, candidate.Part.PartNumber.Value);
                Directory.CreateDirectory(partDir);

                // 成果物をコピー
                var copiedFiles = new List<string>();
                foreach (var (relativePath, _) in candidate.Part.ArtifactHashes)
                {
                    var sourcePath = Path.Combine(candidate.Part.DirectoryPath, relativePath);
                    if (File.Exists(sourcePath))
                    {
                        var destPath = Path.Combine(partDir, relativePath);
                        var destDir = Path.GetDirectoryName(destPath);
                        if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                        {
                            Directory.CreateDirectory(destDir);
                        }
                        File.Copy(sourcePath, destPath, overwrite: true);
                        copiedFiles.Add(relativePath);
                    }
                }

                // パーツ情報ファイルを作成
                var partInfo = CreatePartInfo(candidate.Part);
                var partInfoPath = Path.Combine(partDir, "part_info.txt");
                await File.WriteAllTextAsync(partInfoPath, partInfo, ct);

                manifest.Parts.Add(new DeployManifestPart
                {
                    PartNumber = candidate.Part.PartNumber.Value,
                    Name = candidate.Part.Name,
                    Type = candidate.Part.Type.ToString(),
                    Hash = candidate.Part.CurrentHash,
                    Files = copiedFiles,
                    Reason = candidate.Reason
                });

                result.DeployedParts.Add(candidate.Part.PartNumber.Value);

                // 手配済みとしてマーク
                if (markAsOrdered)
                {
                    await MarkAsOrderedAsync(candidate.Part, ct);
                }
            }
            catch (Exception ex)
            {
                result.Errors[candidate.Part.PartNumber.Value] = ex.Message;
            }
        }

        // マニフェストを作成
        var manifestPath = Path.Combine(packageDir, "manifest.json");
        var manifestJson = JsonSerializer.Serialize(manifest, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        await File.WriteAllTextAsync(manifestPath, manifestJson, ct);

        result.ManifestPath = manifestPath;
        result.PackagePath = packageDir;

        return result;
    }

    /// <summary>
    /// パーツを手配済みとしてマークする。
    /// </summary>
    public async Task MarkAsOrderedAsync(DesignComponent part, CancellationToken ct = default)
    {
        part.ChangeStatus(HandoverStatus.Ordered);

        var record = HandoverRecord.CreateOrder(
            part.Id,
            part.CurrentHash);

        _context.HandoverHistory.Add(record);
        await _context.SaveChangesAsync(ct);
    }

    /// <summary>
    /// パーツを納品済みとしてマークする。
    /// </summary>
    public async Task MarkAsDeliveredAsync(
        string partNumber,
        CancellationToken ct = default)
    {
        var part = await _context.Parts
            .Include(p => p.HandoverRecords)
            .FirstOrDefaultAsync(p => p.PartNumber == new PartNumber(partNumber), ct);

        if (part == null)
        {
            throw new InvalidOperationException($"パーツが見つかりません: {partNumber}");
        }

        part.ChangeStatus(HandoverStatus.Delivered);

        // 最新の手配レコードを更新
        var latestRecord = part.HandoverRecords
            .Where(r => r.Status == HandoverStatus.Ordered)
            .OrderByDescending(r => r.OrderDate)
            .FirstOrDefault();

        if (latestRecord != null)
        {
            latestRecord.MarkAsDelivered();
        }

        await _context.SaveChangesAsync(ct);
    }

    /// <summary>
    /// パーツが手配対象かどうかを判定する。
    /// </summary>
    private static bool ShouldDeploy(DesignComponent part)
    {
        // 未手配（Draft）
        if (part.Status == HandoverStatus.Draft)
        {
            return true;
        }

        // 手配済みだがハッシュが変更されている
        var lastRecord = part.HandoverRecords
            .Where(r => r.Status == HandoverStatus.Ordered || r.Status == HandoverStatus.Delivered)
            .OrderByDescending(r => r.OrderDate)
            .FirstOrDefault();

        if (lastRecord != null && lastRecord.CommittedHash != part.CurrentHash)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// 手配理由を取得する。
    /// </summary>
    private static string GetDeployReason(DesignComponent part)
    {
        if (part.Status == HandoverStatus.Draft)
        {
            return "新規";
        }

        var lastRecord = part.HandoverRecords
            .Where(r => r.Status == HandoverStatus.Ordered || r.Status == HandoverStatus.Delivered)
            .OrderByDescending(r => r.OrderDate)
            .FirstOrDefault();

        if (lastRecord != null && lastRecord.CommittedHash != part.CurrentHash)
        {
            return "変更あり（前回手配後に更新）";
        }

        return "不明";
    }

    /// <summary>
    /// 最後に手配されたハッシュを取得する。
    /// </summary>
    private static string? GetLastOrderedHash(DesignComponent part)
    {
        return part.HandoverRecords
            .Where(r => r.Status == HandoverStatus.Ordered || r.Status == HandoverStatus.Delivered)
            .OrderByDescending(r => r.OrderDate)
            .FirstOrDefault()?.CommittedHash;
    }

    /// <summary>
    /// パーツ情報テキストを作成する。
    /// </summary>
    private static string CreatePartInfo(DesignComponent part)
    {
        var lines = new List<string>
        {
            $"型式: {part.PartNumber}",
            $"名称: {part.Name}",
            $"種別: {part.Type}",
            $"バージョン: {part.Version}",
            $"ハッシュ: {part.CurrentHash}",
            ""
        };

        switch (part)
        {
            case FabricatedPart fab:
                lines.Add($"材質: {fab.Material ?? "-"}");
                lines.Add($"表面処理: {fab.SurfaceTreatment ?? "-"}");
                if (fab.LeadTimeDays.HasValue)
                    lines.Add($"リードタイム: {fab.LeadTimeDays}日");
                break;

            case PurchasedPart pur:
                lines.Add($"メーカー: {pur.Manufacturer ?? "-"}");
                lines.Add($"メーカー型番: {pur.ManufacturerPartNumber ?? "-"}");
                if (pur.LeadTimeDays.HasValue)
                    lines.Add($"リードタイム: {pur.LeadTimeDays}日");
                break;

            case StandardPart std:
                lines.Add($"規格: {std.StandardNumber ?? "-"}");
                lines.Add($"サイズ: {std.Size ?? "-"}");
                lines.Add($"材質等級: {std.MaterialGrade ?? "-"}");
                break;
        }

        if (!string.IsNullOrEmpty(part.Memo))
        {
            lines.Add("");
            lines.Add("備考:");
            lines.Add(part.Memo);
        }

        return string.Join(Environment.NewLine, lines);
    }
}

/// <summary>
/// 手配候補。
/// </summary>
public class DeployCandidate
{
    /// <summary>パーツ</summary>
    public required DesignComponent Part { get; set; }

    /// <summary>手配理由</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>前回手配時のハッシュ</summary>
    public string? LastOrderedHash { get; set; }
}

/// <summary>
/// 手配結果。
/// </summary>
public class DeployResult
{
    /// <summary>出力パス</summary>
    public string OutputPath { get; set; } = string.Empty;

    /// <summary>パッケージパス</summary>
    public string? PackagePath { get; set; }

    /// <summary>マニフェストパス</summary>
    public string? ManifestPath { get; set; }

    /// <summary>手配されたパーツ</summary>
    public List<string> DeployedParts { get; } = new();

    /// <summary>エラー</summary>
    public Dictionary<string, string> Errors { get; } = new();

    /// <summary>成功数</summary>
    public int SuccessCount => DeployedParts.Count;

    /// <summary>エラー数</summary>
    public int ErrorCount => Errors.Count;
}

/// <summary>
/// 手配マニフェスト。
/// </summary>
public class DeployManifest
{
    /// <summary>作成日時</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>パーツリスト</summary>
    public List<DeployManifestPart> Parts { get; set; } = new();
}

/// <summary>
/// マニフェスト内のパーツ情報。
/// </summary>
public class DeployManifestPart
{
    /// <summary>型式</summary>
    public string PartNumber { get; set; } = string.Empty;

    /// <summary>名称</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>種別</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>ハッシュ</summary>
    public string Hash { get; set; } = string.Empty;

    /// <summary>ファイルリスト</summary>
    public List<string> Files { get; set; } = new();

    /// <summary>手配理由</summary>
    public string Reason { get; set; } = string.Empty;
}
