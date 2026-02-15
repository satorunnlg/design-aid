using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using DesignAid.Application.Services;
using DesignAid.Infrastructure.Embedding;
using DesignAid.Infrastructure.Persistence;
using DesignAid.Infrastructure.VectorSearch;

namespace DesignAid.Configuration;

/// <summary>
/// Design Aid サービスの DI 登録を一元管理する拡張メソッド。
/// CLI、Blazor Server、将来の Avalonia UI で共有する。
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Design Aid のコアサービスを登録する。
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    /// <param name="dbPath">SQLite データベースファイルパス</param>
    /// <param name="dataDirectory">データディレクトリパス</param>
    /// <returns>サービスコレクション</returns>
    public static IServiceCollection AddDesignAidServices(
        this IServiceCollection services,
        string dbPath,
        string dataDirectory)
    {
        // DbContextFactory（Blazor Server の長期接続対応）
        services.AddDbContextFactory<DesignAidDbContext>(options =>
            options.UseSqlite($"Data Source={dbPath}"));

        // DbContext（Scoped: CLI や短期スコープ用）
        services.AddDbContext<DesignAidDbContext>(options =>
            options.UseSqlite($"Data Source={dbPath}"),
            ServiceLifetime.Scoped);

        // Singleton: ステートレス or キャッシュ保持
        services.AddSingleton<IHashService, HashService>();
        services.AddSingleton<ISettingsService>(sp =>
        {
            var settingsService = new SettingsService();
            // DB が存在する場合は設定をロード
            if (File.Exists(dbPath))
            {
                using var scope = sp.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<DesignAidDbContext>();
                settingsService.LoadAsync(context).GetAwaiter().GetResult();
            }
            return settingsService;
        });

        // Embedding プロバイダー（設定に応じて切替）
        services.AddSingleton<IEmbeddingProvider>(sp =>
        {
            var settings = sp.GetRequiredService<ISettingsService>();
            var provider = settings.Get("embedding.provider", "Mock");
            var dimensions = settings.GetInt("embedding.dimensions", 384);
            return provider switch
            {
                "OpenAI" => CreateOpenAiProvider(settings),
                "AzureOpenAI" => CreateAzureOpenAiProvider(settings),
                "Ollama" => CreateOllamaProvider(settings, dimensions),
                _ => new MockEmbeddingProvider(dimensions)
            };
        });

        // Scoped: DbContext 依存サービス
        services.AddScoped<IAssetService, AssetService>();
        services.AddScoped<IPartService, PartService>();
        services.AddScoped<IDeployService, DeployService>();

        // ValidationService（HashService 依存）
        services.AddScoped<IValidationService>(sp =>
        {
            var context = sp.GetRequiredService<DesignAidDbContext>();
            var hashService = sp.GetRequiredService<IHashService>();
            return new ValidationService(context, (HashService)hashService);
        });

        // VectorSearchService（Scoped: DbContext + IEmbeddingProvider 依存）
        services.AddScoped<VectorSearchService>(sp =>
        {
            var context = sp.GetRequiredService<DesignAidDbContext>();
            var embeddingProvider = sp.GetRequiredService<IEmbeddingProvider>();
            var settings = sp.GetRequiredService<ISettingsService>();
            var hnswPath = Path.Combine(dataDirectory,
                settings.Get("vector_search.hnsw_index_path", "hnsw_index.bin")!);
            return new VectorSearchService(context, embeddingProvider, hnswPath);
        });

        // SyncService（HashService + VectorSearchService 依存）
        services.AddScoped<ISyncService>(sp =>
        {
            var context = sp.GetRequiredService<DesignAidDbContext>();
            var hashService = sp.GetRequiredService<IHashService>();
            var vectorSearchService = sp.GetService<VectorSearchService>();
            return new SyncService(context, (HashService)hashService, vectorSearchService);
        });

        // SearchService（DbContext + VectorSearchService 依存）
        services.AddScoped<ISearchService>(sp =>
        {
            var context = sp.GetRequiredService<DesignAidDbContext>();
            var vectorSearchService = sp.GetService<VectorSearchService>();
            return new SearchService(context, vectorSearchService);
        });

        return services;
    }

    private static IEmbeddingProvider CreateOpenAiProvider(ISettingsService settings)
    {
        var apiKey = settings.Get("embedding.api_key") ?? "";
        var model = settings.Get("embedding.model") ?? "text-embedding-ada-002";
        return new OpenAiEmbeddingProvider(apiKey, model);
    }

    private static IEmbeddingProvider CreateAzureOpenAiProvider(ISettingsService settings)
    {
        var apiKey = settings.Get("embedding.api_key") ?? "";
        var endpoint = settings.Get("embedding.endpoint") ?? "";
        var model = settings.Get("embedding.model") ?? "text-embedding-ada-002";
        return new AzureOpenAiEmbeddingProvider(endpoint, apiKey, model);
    }

    private static IEmbeddingProvider CreateOllamaProvider(ISettingsService settings, int dimensions)
    {
        var endpoint = settings.Get("embedding.endpoint") ?? "http://localhost:11434";
        var model = settings.Get("embedding.model") ?? "nomic-embed-text";
        return new OllamaEmbeddingProvider(endpoint, model, dimensions);
    }
}
