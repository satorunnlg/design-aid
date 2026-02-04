namespace DesignAid.Infrastructure.Embedding;

/// <summary>
/// 埋め込みベクトル生成のインターフェース。
/// </summary>
public interface IEmbeddingProvider
{
    /// <summary>プロバイダー名</summary>
    string Name { get; }

    /// <summary>ベクトル次元数</summary>
    int Dimensions { get; }

    /// <summary>
    /// テキストをベクトル化する。
    /// </summary>
    /// <param name="text">入力テキスト</param>
    /// <param name="ct">キャンセルトークン</param>
    /// <returns>埋め込みベクトル</returns>
    Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default);

    /// <summary>
    /// 複数テキストを一括ベクトル化する。
    /// </summary>
    /// <param name="texts">入力テキスト群</param>
    /// <param name="ct">キャンセルトークン</param>
    /// <returns>埋め込みベクトル群</returns>
    Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(
        IEnumerable<string> texts,
        CancellationToken ct = default);
}
