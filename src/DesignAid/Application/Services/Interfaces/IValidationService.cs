using DesignAid.Domain.Entities;
using DesignAid.Domain.Standards;
using DesignAid.Domain.ValueObjects;

namespace DesignAid.Application.Services;

/// <summary>
/// 設計基準バリデーションサービスのインターフェース。
/// </summary>
public interface IValidationService
{
    /// <summary>
    /// 利用可能な設計基準一覧を取得する。
    /// </summary>
    IReadOnlyList<IDesignStandard> GetAvailableStandards();

    /// <summary>
    /// 特定の設計基準を取得する。
    /// </summary>
    IDesignStandard? GetStandard(string standardId);

    /// <summary>
    /// 全パーツを検証する。
    /// </summary>
    Task<VerificationResult> VerifyAllAsync(
        string? standardId = null,
        CancellationToken ct = default);

    /// <summary>
    /// 特定のパーツを検証する。
    /// </summary>
    Task<VerificationResult> VerifyPartAsync(
        DesignComponent part,
        string? standardId = null,
        CancellationToken ct = default);

    /// <summary>
    /// パーツの整合性を検証する。
    /// </summary>
    Task<ValidationResult> VerifyIntegrityAsync(
        DesignComponent part,
        CancellationToken ct = default);

    /// <summary>
    /// パーツ番号で検証する。
    /// </summary>
    Task<VerificationResult> VerifyByPartNumberAsync(
        string partNumber,
        string? standardId = null,
        CancellationToken ct = default);

    /// <summary>
    /// 特定の装置内のパーツを検証する。
    /// </summary>
    Task<VerificationResult> VerifyByAssetAsync(
        Guid assetId,
        string? standardId = null,
        CancellationToken ct = default);
}
