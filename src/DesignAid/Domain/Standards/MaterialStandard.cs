using DesignAid.Domain.Entities;
using DesignAid.Domain.ValueObjects;

namespace DesignAid.Domain.Standards;

/// <summary>
/// 材料基準を表す設計基準。
/// 使用材料が承認済みリストに含まれているかを検証する。
/// </summary>
public class MaterialStandard : DesignStandardBase
{
    private readonly HashSet<string> _approvedMaterials;
    private readonly HashSet<string> _conditionalMaterials;

    /// <inheritdoc/>
    public override string StandardId => "STD-MATERIAL-01";

    /// <inheritdoc/>
    public override string Name => "材料基準";

    /// <inheritdoc/>
    public override string? Description => "使用材料が承認済みリストに含まれているかを検証します";

    /// <summary>
    /// 製作物にのみ適用。
    /// </summary>
    protected override ISet<PartType> ApplicableTypes { get; } =
        new HashSet<PartType> { PartType.Fabricated };

    /// <summary>
    /// 材料基準を生成する。
    /// </summary>
    /// <param name="approvedMaterials">承認済み材料リスト</param>
    /// <param name="conditionalMaterials">条件付き承認材料リスト</param>
    public MaterialStandard(
        IEnumerable<string>? approvedMaterials = null,
        IEnumerable<string>? conditionalMaterials = null)
    {
        _approvedMaterials = new HashSet<string>(
            approvedMaterials ?? GetDefaultApprovedMaterials(),
            StringComparer.OrdinalIgnoreCase);

        _conditionalMaterials = new HashSet<string>(
            conditionalMaterials ?? GetDefaultConditionalMaterials(),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc/>
    public override ValidationResult Validate(DesignComponent component)
    {
        var skipResult = CheckApplicability(component);
        if (skipResult != null)
            return skipResult;

        if (component is not FabricatedPart fabricated)
            return ValidationResult.Ok(component.PartNumber);

        var material = fabricated.Material;

        if (string.IsNullOrWhiteSpace(material))
        {
            return ValidationResult.Warning(
                "材料が指定されていません",
                component.PartNumber);
        }

        if (_approvedMaterials.Contains(material))
        {
            return ValidationResult.Ok(component.PartNumber)
                .AddDetail("Material", $"材料 '{material}' は承認済みです", ValidationSeverity.Ok);
        }

        if (_conditionalMaterials.Contains(material))
        {
            return ValidationResult.Warning(
                $"材料 '{material}' は条件付き承認です。構造用途の場合は承認が必要です",
                component.PartNumber)
                .AddDetail("Material", $"条件付き承認材料: {material}", ValidationSeverity.Warning);
        }

        return ValidationResult.Error(
            $"材料 '{material}' は承認されていません",
            component.PartNumber)
            .AddDetail("Material", $"未承認材料: {material}", ValidationSeverity.Error)
            .AddDetail("Recommendation", "承認済み材料を使用するか、材料承認を申請してください",
                ValidationSeverity.Error);
    }

    /// <summary>
    /// デフォルトの承認済み材料リストを取得する。
    /// </summary>
    private static IEnumerable<string> GetDefaultApprovedMaterials()
    {
        return new[]
        {
            // 一般構造用鋼材
            "SS400", "SS490", "SS540",
            // 機械構造用炭素鋼
            "S45C", "S50C", "S55C",
            // ステンレス鋼
            "SUS304", "SUS316", "SUS316L", "SUS303", "SUS430",
            // アルミニウム合金
            "A5052", "A6063", "A7075",
            // 真鍮
            "C3604"
        };
    }

    /// <summary>
    /// デフォルトの条件付き承認材料リストを取得する。
    /// </summary>
    private static IEnumerable<string> GetDefaultConditionalMaterials()
    {
        return new[]
        {
            // 特殊用途材
            "A2017", "A2024",       // 高強度アルミ（耐食性注意）
            "SUS440C",              // 高硬度ステンレス（加工性注意）
            "SKD11", "SKD61",       // 工具鋼
            "SCM435", "SCM440"      // クロムモリブデン鋼
        };
    }
}
