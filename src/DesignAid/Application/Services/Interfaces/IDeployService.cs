using DesignAid.Domain.Entities;

namespace DesignAid.Application.Services;

/// <summary>
/// 手配パッケージ管理サービスのインターフェース。
/// </summary>
public interface IDeployService
{
    /// <summary>
    /// 手配対象のパーツを取得する。
    /// </summary>
    Task<List<DeployCandidate>> GetDeployCandidatesAsync(
        IEnumerable<string>? partNumbers = null,
        CancellationToken ct = default);

    /// <summary>
    /// 手配パッケージを作成する。
    /// </summary>
    Task<DeployResult> CreatePackageAsync(
        string outputPath,
        IEnumerable<string>? partNumbers = null,
        bool markAsOrdered = false,
        CancellationToken ct = default);

    /// <summary>
    /// パーツを手配済みとしてマークする。
    /// </summary>
    Task MarkAsOrderedAsync(DesignComponent part, CancellationToken ct = default);

    /// <summary>
    /// パーツを納品済みとしてマークする。
    /// </summary>
    Task MarkAsDeliveredAsync(string partNumber, CancellationToken ct = default);
}
