using System.Security.Cryptography;
using System.Text;

namespace DesignAid.Infrastructure.Embedding;

/// <summary>
/// 開発・テスト用のモック埋め込みプロバイダー。
/// 実際の埋め込みAPIを呼び出す代わりに、ハッシュベースの疑似ベクトルを生成する。
/// </summary>
public class MockEmbeddingProvider : IEmbeddingProvider
{
    /// <inheritdoc/>
    public string Name => "Mock";

    /// <inheritdoc/>
    public int Dimensions { get; }

    /// <summary>
    /// モックプロバイダーを初期化する。
    /// </summary>
    /// <param name="dimensions">ベクトル次元数（デフォルト: 384）</param>
    public MockEmbeddingProvider(int dimensions = 384)
    {
        Dimensions = dimensions;
    }

    /// <inheritdoc/>
    public Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var embedding = GenerateDeterministicEmbedding(text);
        return Task.FromResult(embedding);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(
        IEnumerable<string> texts,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var embeddings = texts.Select(GenerateDeterministicEmbedding).ToList();
        return Task.FromResult<IReadOnlyList<float[]>>(embeddings);
    }

    /// <summary>
    /// テキストから決定論的な埋め込みベクトルを生成する。
    /// 同じテキストは常に同じベクトルを返す。
    /// </summary>
    private float[] GenerateDeterministicEmbedding(string text)
    {
        // テキストをSHA256でハッシュ化し、そのバイト列からベクトルを生成
        var textBytes = Encoding.UTF8.GetBytes(text.Normalize().ToLowerInvariant());
        var hashBytes = SHA256.HashData(textBytes);

        var embedding = new float[Dimensions];
        var rng = new Random(BitConverter.ToInt32(hashBytes, 0));

        for (int i = 0; i < Dimensions; i++)
        {
            // -1.0 から 1.0 の範囲で値を生成
            embedding[i] = (float)(rng.NextDouble() * 2.0 - 1.0);
        }

        // 正規化（ベクトルの長さを1にする）
        var magnitude = MathF.Sqrt(embedding.Sum(x => x * x));
        if (magnitude > 0)
        {
            for (int i = 0; i < Dimensions; i++)
            {
                embedding[i] /= magnitude;
            }
        }

        // キーワードに基づくバイアス付与（類似テキストが近いベクトルになるよう）
        ApplyKeywordBias(embedding, text);

        return embedding;
    }

    /// <summary>
    /// テキスト内のキーワードに基づいてベクトルにバイアスを付与する。
    /// 同じキーワードを含むテキストは類似度が高くなる。
    /// </summary>
    private void ApplyKeywordBias(float[] embedding, string text)
    {
        var keywords = text.ToLowerInvariant().Split(
            new[] { ' ', '　', '-', '_', '/', '\\', '(', ')', '（', '）' },
            StringSplitOptions.RemoveEmptyEntries);

        foreach (var keyword in keywords)
        {
            if (string.IsNullOrWhiteSpace(keyword) || keyword.Length < 2) continue;

            // キーワードのハッシュから影響を与える次元を決定
            var keywordHash = BitConverter.ToInt32(
                SHA256.HashData(Encoding.UTF8.GetBytes(keyword)), 0);
            var dim = Math.Abs(keywordHash) % Dimensions;

            // その次元の値を強調
            embedding[dim] = Math.Clamp(embedding[dim] + 0.3f, -1.0f, 1.0f);
        }

        // 再正規化
        var magnitude = MathF.Sqrt(embedding.Sum(x => x * x));
        if (magnitude > 0)
        {
            for (int i = 0; i < embedding.Length; i++)
            {
                embedding[i] /= magnitude;
            }
        }
    }
}
