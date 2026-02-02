using DesignAid.Domain.Entities;
using DesignAid.Domain.ValueObjects;

namespace DesignAid.Domain.Standards;

/// <summary>
/// 公差基準を表す設計基準。
/// 製作物の公差指定が適切かを検証する。
/// </summary>
public class ToleranceStandard : DesignStandardBase
{
    /// <inheritdoc/>
    public override string StandardId => "STD-TOLERANCE-01";

    /// <inheritdoc/>
    public override string Name => "公差基準";

    /// <inheritdoc/>
    public override string? Description => "製作物の公差指定が適切かを検証します";

    /// <summary>
    /// 製作物にのみ適用。
    /// </summary>
    protected override ISet<PartType> ApplicableTypes { get; } =
        new HashSet<PartType> { PartType.Fabricated };

    /// <inheritdoc/>
    public override ValidationResult Validate(DesignComponent component)
    {
        var skipResult = CheckApplicability(component);
        if (skipResult != null)
            return skipResult;

        if (component is not FabricatedPart fabricated)
            return ValidationResult.Ok(component.PartNumber);

        var details = new List<ValidationDetail>();

        // メタデータから公差関連情報をチェック
        if (fabricated.Metadata.TryGetValue("general_tolerance", out var toleranceObj)
            && toleranceObj is string tolerance)
        {
            var validTolerances = new[] { "JIS B 0405-m", "JIS B 0405-c", "JIS B 0405-v", "JIS B 0405-f" };

            if (!validTolerances.Contains(tolerance, StringComparer.OrdinalIgnoreCase))
            {
                details.Add(new ValidationDetail(
                    "GeneralTolerance",
                    $"一般公差 '{tolerance}' は標準規格外です",
                    ValidationSeverity.Warning));
            }
            else
            {
                details.Add(new ValidationDetail(
                    "GeneralTolerance",
                    $"一般公差 '{tolerance}' は適切です",
                    ValidationSeverity.Ok));
            }
        }

        // 表面粗さチェック
        if (fabricated.Metadata.TryGetValue("surface_roughness", out var roughnessObj)
            && roughnessObj is string roughness)
        {
            // Ra値のパース試行
            if (TryParseRaValue(roughness, out var raValue))
            {
                if (raValue < 0.8)
                {
                    details.Add(new ValidationDetail(
                        "SurfaceRoughness",
                        $"表面粗さ Ra{raValue} は高精度仕上げが必要です（追加コスト注意）",
                        ValidationSeverity.Warning));
                }
                else
                {
                    details.Add(new ValidationDetail(
                        "SurfaceRoughness",
                        $"表面粗さ Ra{raValue} は適切です",
                        ValidationSeverity.Ok));
                }
            }
        }

        if (details.Count == 0)
        {
            return ValidationResult.Ok(component.PartNumber)
                .AddDetail("Tolerance", "公差情報が指定されていません（メタデータで指定可能）",
                    ValidationSeverity.Ok);
        }

        var maxSeverity = details.Max(d => d.Severity);
        var message = maxSeverity switch
        {
            ValidationSeverity.Error => "公差基準に違反があります",
            ValidationSeverity.Warning => "公差指定に注意が必要です",
            _ => "公差は適切です"
        };

        return ValidationResult.WithDetails(maxSeverity, message, details, component.PartNumber);
    }

    /// <summary>
    /// Ra値の文字列をパースする。
    /// </summary>
    private static bool TryParseRaValue(string roughness, out double raValue)
    {
        raValue = 0;

        // "Ra1.6" や "1.6" などの形式を処理
        var normalized = roughness
            .Replace("Ra", "", StringComparison.OrdinalIgnoreCase)
            .Replace("μm", "", StringComparison.OrdinalIgnoreCase)
            .Trim();

        return double.TryParse(normalized, out raValue);
    }
}
