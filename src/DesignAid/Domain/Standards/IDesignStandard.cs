using DesignAid.Domain.Entities;
using DesignAid.Domain.ValueObjects;
using DesignAid.Infrastructure.FileSystem;

namespace DesignAid.Domain.Standards;

/// <summary>
/// 設計基準バリデーション結果。
/// </summary>
public record StandardValidationResult
{
    /// <summary>検証がパスしたかどうか</summary>
    public bool IsPass { get; init; }

    /// <summary>メッセージ</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>推奨事項</summary>
    public string? Recommendation { get; init; }

    public static StandardValidationResult Pass(string message) =>
        new() { IsPass = true, Message = message };

    public static StandardValidationResult Fail(string message, string? recommendation = null) =>
        new() { IsPass = false, Message = message, Recommendation = recommendation };
}

/// <summary>
/// 設計基準（理）を定義するインターフェース。
/// 各基準はこのインターフェースを実装し、パーツのバリデーションを提供する。
/// </summary>
public interface IDesignStandard
{
    /// <summary>基準ID</summary>
    string StandardId { get; }

    /// <summary>基準名</summary>
    string Name { get; }

    /// <summary>基準の説明</summary>
    string? Description { get; }

    /// <summary>
    /// パーツに対してバリデーションを実行する。
    /// </summary>
    /// <param name="component">検証対象のパーツ</param>
    /// <returns>バリデーション結果</returns>
    ValidationResult Validate(DesignComponent component);

    /// <summary>
    /// PartJson に対してバリデーションを実行する。
    /// </summary>
    /// <param name="partJson">検証対象のパーツJSON</param>
    /// <returns>バリデーション結果</returns>
    StandardValidationResult Validate(PartJson partJson);

    /// <summary>
    /// この基準が指定されたパーツ種別に適用可能かどうかを判定する。
    /// </summary>
    /// <param name="partType">パーツ種別</param>
    /// <returns>適用可能な場合は true</returns>
    bool IsApplicableTo(PartType partType);
}

/// <summary>
/// 設計基準の基底抽象クラス。
/// 共通の実装を提供する。
/// </summary>
public abstract class DesignStandardBase : IDesignStandard
{
    /// <inheritdoc/>
    public abstract string StandardId { get; }

    /// <inheritdoc/>
    public abstract string Name { get; }

    /// <inheritdoc/>
    public virtual string? Description => null;

    /// <summary>
    /// 適用可能なパーツ種別のセット。
    /// </summary>
    protected virtual ISet<PartType> ApplicableTypes { get; } =
        new HashSet<PartType> { PartType.Fabricated, PartType.Purchased, PartType.Standard };

    /// <inheritdoc/>
    public abstract ValidationResult Validate(DesignComponent component);

    /// <inheritdoc/>
    public abstract StandardValidationResult Validate(PartJson partJson);

    /// <inheritdoc/>
    public virtual bool IsApplicableTo(PartType partType) => ApplicableTypes.Contains(partType);

    /// <summary>
    /// パーツが適用可能かどうかをチェックし、適用不可の場合はスキップ結果を返す。
    /// </summary>
    protected ValidationResult? CheckApplicability(DesignComponent component)
    {
        if (!IsApplicableTo(component.Type))
        {
            return ValidationResult.Ok(component.PartNumber)
                .AddDetail("Applicability", $"基準 '{Name}' は {component.Type} には適用されません",
                    ValidationSeverity.Ok);
        }

        return null;
    }

    /// <summary>
    /// PartJson が適用可能かどうかをチェックし、適用不可の場合はスキップ結果を返す。
    /// </summary>
    protected StandardValidationResult? CheckApplicability(PartJson partJson)
    {
        var partType = PartJsonReader.ParsePartType(partJson);
        if (!IsApplicableTo(partType))
        {
            return StandardValidationResult.Pass($"基準 '{Name}' は {partType} には適用されません");
        }

        return null;
    }
}
