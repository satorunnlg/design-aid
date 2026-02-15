using DesignAid.Domain.Entities;

namespace DesignAid.Application.Services;

/// <summary>
/// ファイルシステムとDBの同期サービスのインターフェース。
/// </summary>
public interface ISyncService
{
    /// <summary>
    /// 全パーツを同期する。
    /// </summary>
    Task<SyncResult> SyncAllAsync(
        bool force = false,
        bool includeVectors = false,
        CancellationToken ct = default);

    /// <summary>
    /// 特定のパーツを同期する。
    /// </summary>
    Task<SyncResult> SyncPartAsync(
        DesignComponent part,
        bool force = false,
        CancellationToken ct = default);

    /// <summary>
    /// ベクトルインデックスにパーツを同期する。
    /// </summary>
    Task<int> SyncToVectorIndexAsync(CancellationToken ct = default);
}
