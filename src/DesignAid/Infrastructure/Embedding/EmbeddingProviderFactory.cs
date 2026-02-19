using DesignAid.Application.Services;

namespace DesignAid.Infrastructure.Embedding;

/// <summary>
/// 設定に基づいて適切な IEmbeddingProvider を生成するファクトリ。
/// </summary>
public static class EmbeddingProviderFactory
{
    /// <summary>
    /// SettingsService の設定値から IEmbeddingProvider を生成する。
    /// </summary>
    /// <param name="settings">設定サービス</param>
    /// <returns>設定に対応する IEmbeddingProvider</returns>
    public static IEmbeddingProvider Create(SettingsService settings)
    {
        var provider = settings.Get("embedding.provider", "mock")!.ToLowerInvariant();
        var dimensions = settings.GetInt("embedding.dimensions", 384);

        return provider switch
        {
            "mock" => new MockEmbeddingProvider(dimensions),

            "ollama" => new OllamaEmbeddingProvider(
                host: settings.Get("embedding.endpoint", "http://localhost:11434")!,
                model: settings.Get("embedding.model", "nomic-embed-text")!,
                dimensions: dimensions),

            "openai" => new OpenAiEmbeddingProvider(
                apiKey: ResolveApiKey(settings),
                model: settings.Get("embedding.model", "text-embedding-3-small")!,
                dimensions: dimensions),

            "azure_openai" or "azure" => new AzureOpenAiEmbeddingProvider(
                endpoint: settings.Get("embedding.endpoint")
                    ?? throw new InvalidOperationException(
                        "Azure OpenAI にはエンドポイントが必要です。`daid config set embedding.endpoint <url>` で設定してください"),
                apiKey: ResolveApiKey(settings),
                deploymentName: settings.Get("embedding.model")
                    ?? throw new InvalidOperationException(
                        "Azure OpenAI にはデプロイメント名が必要です。`daid config set embedding.model <name>` で設定してください"),
                dimensions: dimensions),

            _ => throw new InvalidOperationException(
                $"未知の埋め込みプロバイダー: {provider}。有効な値: mock, ollama, openai, azure_openai")
        };
    }

    /// <summary>
    /// API キーを設定値または環境変数から解決する。
    /// </summary>
    private static string ResolveApiKey(SettingsService settings)
    {
        var apiKey = settings.Get("embedding.api_key");

        if (string.IsNullOrEmpty(apiKey))
        {
            // 環境変数フォールバック
            apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
                  ?? Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY");
        }

        if (string.IsNullOrEmpty(apiKey))
        {
            throw new InvalidOperationException(
                "API キーが設定されていません。`daid config set embedding.api_key <key>` または環境変数 OPENAI_API_KEY を設定してください");
        }

        return apiKey;
    }
}
