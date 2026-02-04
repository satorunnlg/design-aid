using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace DesignAid.Infrastructure.Embedding;

/// <summary>
/// Ollama を使用した埋め込みプロバイダー。
/// ローカル実行用。
/// </summary>
public class OllamaEmbeddingProvider : IEmbeddingProvider, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _model;
    private readonly int _dimensions;
    private readonly bool _ownsHttpClient;

    /// <summary>プロバイダー名</summary>
    public string Name => "Ollama";

    /// <summary>ベクトル次元数</summary>
    public int Dimensions => _dimensions;

    /// <summary>
    /// OllamaEmbeddingProvider を初期化する。
    /// </summary>
    /// <param name="host">Ollama ホストURL（デフォルト: http://localhost:11434）</param>
    /// <param name="model">使用モデル（デフォルト: nomic-embed-text）</param>
    /// <param name="dimensions">ベクトル次元数（デフォルト: 768）</param>
    /// <param name="httpClient">HTTPクライアント（省略時は内部作成）</param>
    public OllamaEmbeddingProvider(
        string host = "http://localhost:11434",
        string model = "nomic-embed-text",
        int dimensions = 768,
        HttpClient? httpClient = null)
    {
        _model = model;
        _dimensions = dimensions;
        _ownsHttpClient = httpClient == null;

        _httpClient = httpClient ?? new HttpClient();
        _httpClient.BaseAddress ??= new Uri(host.TrimEnd('/') + "/");
        _httpClient.Timeout = TimeSpan.FromMinutes(5); // Ollama は遅いことがある
    }

    /// <summary>
    /// テキストをベクトル化する。
    /// </summary>
    public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new float[_dimensions];

        var request = new OllamaEmbeddingRequest
        {
            Model = _model,
            Prompt = text
        };

        var response = await _httpClient.PostAsJsonAsync("api/embeddings", request, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<OllamaEmbeddingResponse>(ct);

        if (result?.Embedding == null || result.Embedding.Length == 0)
            throw new InvalidOperationException("Ollama からの埋め込みレスポンスが空です");

        return result.Embedding;
    }

    /// <summary>
    /// 複数テキストを一括ベクトル化する。
    /// Ollama はバッチ API がないため、1件ずつ処理する。
    /// </summary>
    public async Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(
        IEnumerable<string> texts,
        CancellationToken ct = default)
    {
        var results = new List<float[]>();

        foreach (var text in texts)
        {
            ct.ThrowIfCancellationRequested();
            var embedding = await GenerateEmbeddingAsync(text, ct);
            results.Add(embedding);
        }

        return results;
    }

    /// <summary>
    /// Ollama サーバーが利用可能かチェックする。
    /// </summary>
    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync("api/tags", ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 指定モデルがインストールされているかチェックする。
    /// </summary>
    public async Task<bool> IsModelAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync("api/tags", ct);
            if (!response.IsSuccessStatusCode)
                return false;

            var result = await response.Content.ReadFromJsonAsync<OllamaTagsResponse>(ct);
            return result?.Models?.Any(m =>
                m.Name?.StartsWith(_model, StringComparison.OrdinalIgnoreCase) == true) ?? false;
        }
        catch
        {
            return false;
        }
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
/// Ollama 埋め込みリクエスト。
/// </summary>
internal class OllamaEmbeddingRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("prompt")]
    public string Prompt { get; set; } = string.Empty;
}

/// <summary>
/// Ollama 埋め込みレスポンス。
/// </summary>
internal class OllamaEmbeddingResponse
{
    [JsonPropertyName("embedding")]
    public float[] Embedding { get; set; } = [];
}

/// <summary>
/// Ollama タグ一覧レスポンス。
/// </summary>
internal class OllamaTagsResponse
{
    [JsonPropertyName("models")]
    public List<OllamaModelInfo>? Models { get; set; }
}

/// <summary>
/// Ollama モデル情報。
/// </summary>
internal class OllamaModelInfo
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("digest")]
    public string? Digest { get; set; }
}
