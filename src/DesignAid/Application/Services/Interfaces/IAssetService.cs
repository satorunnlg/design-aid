using DesignAid.Domain.Entities;

namespace DesignAid.Application.Services;

/// <summary>
/// 装置管理サービスのインターフェース。
/// CLI、Blazor Server、将来の Avalonia UI で共有する。
/// </summary>
public interface IAssetService
{
    /// <summary>
    /// 装置を追加する。
    /// </summary>
    Task<Asset> AddAsync(
        string name,
        string directoryPath,
        string? displayName = null,
        string? description = null,
        CancellationToken ct = default);

    /// <summary>
    /// 全装置一覧を取得する。
    /// </summary>
    Task<List<Asset>> GetAllAsync(CancellationToken ct = default);

    /// <summary>
    /// 装置をIDで取得する。
    /// </summary>
    Task<Asset?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// 装置を名前で取得する。
    /// </summary>
    Task<Asset?> GetByNameAsync(string name, CancellationToken ct = default);

    /// <summary>
    /// 装置を削除する。
    /// </summary>
    Task<bool> RemoveAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// 装置を更新する。
    /// </summary>
    Task<Asset?> UpdateAsync(
        Guid id,
        string? displayName = null,
        string? description = null,
        CancellationToken ct = default);

    /// <summary>
    /// 子装置を追加する。
    /// </summary>
    Task<AssetSubAsset> AddSubAssetAsync(
        Guid parentAssetId,
        Guid childAssetId,
        int quantity = 1,
        string? notes = null,
        CancellationToken ct = default);

    /// <summary>
    /// 子装置を削除する。
    /// </summary>
    Task<bool> RemoveSubAssetAsync(
        Guid parentAssetId,
        Guid childAssetId,
        CancellationToken ct = default);
}
