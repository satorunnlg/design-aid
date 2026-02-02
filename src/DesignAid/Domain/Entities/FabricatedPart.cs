using DesignAid.Domain.ValueObjects;

namespace DesignAid.Domain.Entities;

/// <summary>
/// 製作物（図面品）を表すエンティティ。
/// 自社が設計責任を持ち、図面を正とする。
/// </summary>
public class FabricatedPart : DesignComponent
{
    /// <inheritdoc/>
    public override PartType Type => PartType.Fabricated;

    /// <summary>材質</summary>
    public string? Material { get; set; }

    /// <summary>表面処理</summary>
    public string? SurfaceTreatment { get; set; }

    /// <summary>図面ファイルの相対パス</summary>
    public string? DrawingPath { get; set; }

    /// <summary>加工リードタイム（日）</summary>
    public int? LeadTimeDays { get; set; }

    /// <summary>
    /// EF Core 用のパラメータなしコンストラクタ。
    /// </summary>
    protected FabricatedPart() { }

    /// <summary>
    /// 新しい製作物を生成する。
    /// </summary>
    /// <param name="partNumber">型式</param>
    /// <param name="name">パーツ名</param>
    /// <param name="directoryPath">ディレクトリパス</param>
    /// <returns>FabricatedPart インスタンス</returns>
    public static FabricatedPart Create(
        PartNumber partNumber,
        string name,
        string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("パーツ名は必須です", nameof(name));

        if (string.IsNullOrWhiteSpace(directoryPath))
            throw new ArgumentException("ディレクトリパスは必須です", nameof(directoryPath));

        var now = DateTime.UtcNow;

        return new FabricatedPart
        {
            Id = Guid.NewGuid(),
            PartNumber = partNumber,
            Name = name,
            DirectoryPath = Path.GetFullPath(directoryPath),
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    /// <summary>
    /// 既存の製作物を再構築する（DB から読み込み時）。
    /// </summary>
    public static FabricatedPart Reconstruct(
        Guid id,
        PartNumber partNumber,
        string name,
        string directoryPath,
        string version,
        string currentHash,
        HandoverStatus status,
        DateTime createdAt,
        DateTime updatedAt,
        string? material = null,
        string? surfaceTreatment = null,
        string? drawingPath = null,
        int? leadTimeDays = null)
    {
        return new FabricatedPart
        {
            Id = id,
            PartNumber = partNumber,
            Name = name,
            DirectoryPath = directoryPath,
            Version = version,
            CurrentHash = currentHash,
            Status = status,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
            Material = material,
            SurfaceTreatment = surfaceTreatment,
            DrawingPath = drawingPath,
            LeadTimeDays = leadTimeDays
        };
    }

    /// <inheritdoc/>
    public override ValidationResult ValidateIntegrity()
    {
        var details = new List<ValidationDetail>();

        // 図面ファイルの存在確認
        if (!string.IsNullOrEmpty(DrawingPath))
        {
            var fullDrawingPath = Path.Combine(DirectoryPath, DrawingPath);
            if (!File.Exists(fullDrawingPath))
            {
                details.Add(new ValidationDetail(
                    "DrawingPath",
                    $"図面ファイルが見つかりません: {DrawingPath}",
                    ValidationSeverity.Error));
            }
        }

        // 成果物ハッシュの検証
        foreach (var (relativePath, expectedHash) in ArtifactHashes)
        {
            var fullPath = Path.Combine(DirectoryPath, relativePath);
            if (!File.Exists(fullPath))
            {
                details.Add(new ValidationDetail(
                    relativePath,
                    $"成果物ファイルが見つかりません: {relativePath}",
                    ValidationSeverity.Error));
            }
            // 実際のハッシュ検証は HashService で行う
        }

        if (details.Count == 0)
        {
            return ValidationResult.Ok(PartNumber);
        }

        var maxSeverity = details.Max(d => d.Severity);
        return ValidationResult.WithDetails(
            maxSeverity,
            $"製作物 '{PartNumber}' に問題があります",
            details,
            PartNumber);
    }

    /// <summary>
    /// 製作物固有の情報を更新する。
    /// </summary>
    public void UpdateFabricationInfo(
        string? material = null,
        string? surfaceTreatment = null,
        string? drawingPath = null,
        int? leadTimeDays = null)
    {
        Material = material ?? Material;
        SurfaceTreatment = surfaceTreatment ?? SurfaceTreatment;
        DrawingPath = drawingPath ?? DrawingPath;
        LeadTimeDays = leadTimeDays ?? LeadTimeDays;
        UpdatedAt = DateTime.UtcNow;
    }
}
