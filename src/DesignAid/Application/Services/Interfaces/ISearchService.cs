using DesignAid.Infrastructure.VectorSearch;

namespace DesignAid.Application.Services;

/// <summary>
/// 類似設計検索サービスのインターフェース。
/// </summary>
public interface ISearchService
{
    /// <summary>
    /// ベクトルインデックスが利用可能かどうかを確認する。
    /// </summary>
    Task<bool> IsVectorIndexAvailableAsync(CancellationToken ct = default);

    /// <summary>
    /// パーツをベクトルインデックスに同期する。
    /// </summary>
    Task<int> SyncToVectorIndexAsync(Guid? partId = null, CancellationToken ct = default);

    /// <summary>
    /// ベクトル検索を行う。
    /// </summary>
    Task<List<SearchResultDto>> SearchAsync(
        string query,
        double threshold = 0.7,
        int limit = 10,
        CancellationToken ct = default);

    /// <summary>
    /// パーツをベクトルインデックスから削除する。
    /// </summary>
    Task RemoveFromVectorIndexAsync(Guid partId, CancellationToken ct = default);

    /// <summary>
    /// ベクトルインデックスをクリアする。
    /// </summary>
    Task ClearVectorIndexAsync(CancellationToken ct = default);

    /// <summary>
    /// ベクトルインデックスの統計情報を取得する。
    /// </summary>
    Task<VectorIndexStats> GetStatsAsync(CancellationToken ct = default);
}
