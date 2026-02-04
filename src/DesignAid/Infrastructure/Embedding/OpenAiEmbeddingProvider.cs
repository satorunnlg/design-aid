using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DesignAid.Infrastructure.Embedding;

/// <summary>
/// OpenAI API を使用した埋め込みプロバイダー。
/// </summary>
public class OpenAiEmbeddingProvider : IEmbeddingProvider, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _model;
    private readonly int _dimensions;
    private readonly bool _ownsHttpClient;

    /// <summary>プロバイダー名</summary>
    public string Name => "OpenAI";

    /// <summary>ベクトル次元数</summary>
    public int Dimensions => _dimensions;

    /// <summary>
    /// OpenAiEmbeddingProvider を初期化する。
    /// </summary>
    /// <param name="apiKey">OpenAI API キー</param>
    /// <param name="model">使用モデル（デフォルト: text-embedding-3-small）</param>
    /// <param name="dimensions">ベクトル次元数（デフォルト: 1536）</param>
    /// <param name="httpClient">HTTPクライアント（省略時は内部作成）</param>
    public OpenAiEmbeddingProvider(
        string apiKey,
        string model = "text-embedding-3-small",
        int dimensions = 1536,
        HttpClient? httpClient = null)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("API キーは必須です", nameof(apiKey));

        _model = model;
        _dimensions = dimensions;
        _ownsHttpClient = httpClient == null;

        _httpClient = httpClient ?? new HttpClient();
        _httpClient.BaseAddress ??= new Uri("https://api.openai.com/v1/");
        _httpClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
    }

    /// <summary>
    /// テキストをベクトル化する。
    /// </summary>
    public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new float[_dimensions];

        var request = new OpenAiEmbeddingRequest
        {
            Input = [text],
            Model = _model,
            Dimensions = _dimensions
        };

        var response = await _httpClient.PostAsJsonAsync("embeddings", request, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<OpenAiEmbeddingResponse>(ct);

        if (result?.Data == null || result.Data.Count == 0)
            throw new InvalidOperationException("埋め込みレスポンスが空です");

        return result.Data[0].Embedding;
    }

    /// <summary>
    /// 複数テキストを一括ベクトル化する。
    /// </summary>
    public async Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(
        IEnumerable<string> texts,
        CancellationToken ct = default)
    {
        var textList = texts.ToList();
        if (textList.Count == 0)
            return [];

        // 空文字列はダミーに置換
        var processedTexts = textList
            .Select(t => string.IsNullOrWhiteSpace(t) ? " " : t)
            .ToList();

        var request = new OpenAiEmbeddingRequest
        {
            Input = processedTexts,
            Model = _model,
            Dimensions = _dimensions
        };

        var response = await _httpClient.PostAsJsonAsync("embeddings", request, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<OpenAiEmbeddingResponse>(ct);

        if (result?.Data == null)
            throw new InvalidOperationException("埋め込みレスポンスが空です");

        // インデックス順にソートして返す
        return result.Data
            .OrderBy(d => d.Index)
            .Select(d => d.Embedding)
            .ToList();
    }

    /// <summary>
    /// リソースを解放する。
    /// </summary>
    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// OpenAI 埋め込みリクエスト。
/// </summary>
internal class OpenAiEmbeddingRequest
{
    [JsonPropertyName("input")]
    public List<string> Input { get; set; } = [];

    [JsonPropertyName("model")]
    public string Model { get; set; } = "text-embedding-3-small";

    [JsonPropertyName("dimensions")]
    public int Dimensions { get; set; } = 1536;
}

/// <summary>
/// OpenAI 埋め込みレスポンス。
/// </summary>
internal class OpenAiEmbeddingResponse
{
    [JsonPropertyName("object")]
    public string? Object { get; set; }

    [JsonPropertyName("data")]
    public List<OpenAiEmbeddingData> Data { get; set; } = [];

    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("usage")]
    public OpenAiUsage? Usage { get; set; }
}

/// <summary>
/// 埋め込みデータ。
/// </summary>
internal class OpenAiEmbeddingData
{
    [JsonPropertyName("object")]
    public string? Object { get; set; }

    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("embedding")]
    public float[] Embedding { get; set; } = [];
}

/// <summary>
/// API 使用量情報。
/// </summary>
internal class OpenAiUsage
{
    [JsonPropertyName("prompt_tokens")]
    public int PromptTokens { get; set; }

    [JsonPropertyName("total_tokens")]
    public int TotalTokens { get; set; }
}
