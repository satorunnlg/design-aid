using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace DesignAid.Infrastructure.Embedding;

/// <summary>
/// Azure OpenAI Service を使用した埋め込みプロバイダー。
/// </summary>
public class AzureOpenAiEmbeddingProvider : IEmbeddingProvider, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _deploymentName;
    private readonly int _dimensions;
    private readonly bool _ownsHttpClient;
    private readonly string _apiVersion;

    /// <summary>プロバイダー名</summary>
    public string Name => "Azure";

    /// <summary>ベクトル次元数</summary>
    public int Dimensions => _dimensions;

    /// <summary>
    /// AzureOpenAiEmbeddingProvider を初期化する。
    /// </summary>
    /// <param name="endpoint">Azure OpenAI エンドポイント（例: https://your-resource.openai.azure.com）</param>
    /// <param name="apiKey">Azure OpenAI API キー</param>
    /// <param name="deploymentName">デプロイメント名</param>
    /// <param name="dimensions">ベクトル次元数（デフォルト: 1536）</param>
    /// <param name="apiVersion">API バージョン（デフォルト: 2024-02-01）</param>
    /// <param name="httpClient">HTTPクライアント（省略時は内部作成）</param>
    public AzureOpenAiEmbeddingProvider(
        string endpoint,
        string apiKey,
        string deploymentName,
        int dimensions = 1536,
        string apiVersion = "2024-02-01",
        HttpClient? httpClient = null)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            throw new ArgumentException("エンドポイントは必須です", nameof(endpoint));
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("API キーは必須です", nameof(apiKey));
        if (string.IsNullOrWhiteSpace(deploymentName))
            throw new ArgumentException("デプロイメント名は必須です", nameof(deploymentName));

        _deploymentName = deploymentName;
        _dimensions = dimensions;
        _apiVersion = apiVersion;
        _ownsHttpClient = httpClient == null;

        _httpClient = httpClient ?? new HttpClient();
        _httpClient.BaseAddress ??= new Uri(endpoint.TrimEnd('/') + "/");
        _httpClient.DefaultRequestHeaders.Add("api-key", apiKey);
    }

    /// <summary>
    /// テキストをベクトル化する。
    /// </summary>
    public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new float[_dimensions];

        var request = new AzureEmbeddingRequest
        {
            Input = [text]
        };

        var requestUri = $"openai/deployments/{_deploymentName}/embeddings?api-version={_apiVersion}";
        var response = await _httpClient.PostAsJsonAsync(requestUri, request, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<AzureEmbeddingResponse>(ct);

        if (result?.Data == null || result.Data.Count == 0)
            throw new InvalidOperationException("Azure OpenAI からの埋め込みレスポンスが空です");

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

        var request = new AzureEmbeddingRequest
        {
            Input = processedTexts
        };

        var requestUri = $"openai/deployments/{_deploymentName}/embeddings?api-version={_apiVersion}";
        var response = await _httpClient.PostAsJsonAsync(requestUri, request, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<AzureEmbeddingResponse>(ct);

        if (result?.Data == null)
            throw new InvalidOperationException("Azure OpenAI からの埋め込みレスポンスが空です");

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
/// Azure OpenAI 埋め込みリクエスト。
/// </summary>
internal class AzureEmbeddingRequest
{
    [JsonPropertyName("input")]
    public List<string> Input { get; set; } = [];
}

/// <summary>
/// Azure OpenAI 埋め込みレスポンス。
/// </summary>
internal class AzureEmbeddingResponse
{
    [JsonPropertyName("object")]
    public string? Object { get; set; }

    [JsonPropertyName("data")]
    public List<AzureEmbeddingData> Data { get; set; } = [];

    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("usage")]
    public AzureUsage? Usage { get; set; }
}

/// <summary>
/// Azure 埋め込みデータ。
/// </summary>
internal class AzureEmbeddingData
{
    [JsonPropertyName("object")]
    public string? Object { get; set; }

    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("embedding")]
    public float[] Embedding { get; set; } = [];
}

/// <summary>
/// Azure API 使用量情報。
/// </summary>
internal class AzureUsage
{
    [JsonPropertyName("prompt_tokens")]
    public int PromptTokens { get; set; }

    [JsonPropertyName("total_tokens")]
    public int TotalTokens { get; set; }
}
