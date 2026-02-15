using DesignAid.Domain.Entities;

namespace DesignAid.Application.Services;

/// <summary>
/// パーツ管理サービスのインターフェース。
/// CLI、Blazor Server、将来の Avalonia UI で共有する。
/// </summary>
public interface IPartService
{
    /// <summary>
    /// 製作物パーツを追加する。
    /// </summary>
    Task<FabricatedPart> AddFabricatedPartAsync(
        string partNumber,
        string name,
        string directoryPath,
        string? material = null,
        string? surfaceTreatment = null,
        string? memo = null,
        CancellationToken ct = default);

    /// <summary>
    /// 購入品パーツを追加する。
    /// </summary>
    Task<PurchasedPart> AddPurchasedPartAsync(
        string partNumber,
        string name,
        string directoryPath,
        string? manufacturer = null,
        string? modelNumber = null,
        string? memo = null,
        CancellationToken ct = default);

    /// <summary>
    /// 規格品パーツを追加する。
    /// </summary>
    Task<StandardPart> AddStandardPartAsync(
        string partNumber,
        string name,
        string directoryPath,
        string? standardCode = null,
        string? size = null,
        string? memo = null,
        CancellationToken ct = default);

    /// <summary>
    /// パーツ一覧を取得する。
    /// </summary>
    Task<List<DesignComponent>> GetAllAsync(CancellationToken ct = default);

    /// <summary>
    /// 種別でパーツを取得する。
    /// </summary>
    Task<List<DesignComponent>> GetByTypeAsync(PartType type, CancellationToken ct = default);

    /// <summary>
    /// パーツを型式で取得する。
    /// </summary>
    Task<DesignComponent?> GetByPartNumberAsync(string partNumber, CancellationToken ct = default);

    /// <summary>
    /// パーツをIDで取得する。
    /// </summary>
    Task<DesignComponent?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// パーツを削除する。
    /// </summary>
    Task<bool> RemoveAsync(string partNumber, CancellationToken ct = default);

    /// <summary>
    /// パーツを装置にリンクする。
    /// </summary>
    Task<AssetComponent> LinkToAssetAsync(
        Guid assetId,
        Guid partId,
        int quantity = 1,
        string? notes = null,
        CancellationToken ct = default);

    /// <summary>
    /// 装置に紐づくパーツを取得する。
    /// </summary>
    Task<List<(DesignComponent Part, AssetComponent Link)>> GetPartsByAssetAsync(
        Guid assetId,
        CancellationToken ct = default);
}
