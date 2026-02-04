using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using DesignAid.Application.Services;
using DesignAid.Infrastructure.Embedding;
using DesignAid.Infrastructure.Persistence;
using DesignAid.Infrastructure.Qdrant;

namespace DesignAid.Configuration;

/// <summary>
/// DI（依存性注入）の設定を行う拡張メソッド群。
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Design Aid のサービスを登録する。
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    /// <param name="configuration">設定</param>
    /// <returns>サービスコレクション</returns>
    public static IServiceCollection AddDesignAid(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // 設定をバインド
        var settings = new AppSettings();
        configuration.GetSection(AppSettings.SectionName).Bind(settings);
        services.AddSingleton(settings);

        // DbContext
        services.AddDbContext<DesignAidDbContext>(options =>
        {
            var dbPath = settings.GetDatabasePath();
            var directory = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            options.UseSqlite($"Data Source={dbPath}");
        });

        // 埋め込みプロバイダー
        services.AddEmbeddingProvider(settings);

        // Qdrant サービス
        services.AddQdrantService(settings);

        // アプリケーションサービス
        services.AddScoped<AssetService>();
        services.AddScoped<PartService>();
        services.AddScoped<HashService>();
        services.AddScoped<SearchService>();
        services.AddScoped<SyncService>();
        services.AddScoped<ValidationService>();
        services.AddScoped<DeployService>();

        return services;
    }

    /// <summary>
    /// 埋め込みプロバイダーを登録する。
    /// </summary>
    private static IServiceCollection AddEmbeddingProvider(
        this IServiceCollection services,
        AppSettings settings)
    {
        var providerName = settings.Embedding.GetProvider();
        var providerSettings = settings.Embedding.GetCurrentProviderSettings();

        // プロバイダーに応じて登録
        services.AddSingleton<IEmbeddingProvider>(sp =>
        {
            return providerName.ToLowerInvariant() switch
            {
                "mock" => new MockEmbeddingProvider(providerSettings?.Dimensions ?? 384),
                // 将来的に OpenAI, Ollama, Azure を追加
                // "openai" => new OpenAiEmbeddingProvider(...),
                // "ollama" => new OllamaEmbeddingProvider(...),
                // "azure" => new AzureEmbeddingProvider(...),
                _ => new MockEmbeddingProvider(providerSettings?.Dimensions ?? 384)
            };
        });

        return services;
    }

    /// <summary>
    /// Qdrant サービスを登録する。
    /// </summary>
    private static IServiceCollection AddQdrantService(
        this IServiceCollection services,
        AppSettings settings)
    {
        // Qdrant 無効時は登録しない（GetService<QdrantService>() で null を取得可能）
        if (!settings.Qdrant.IsEnabled())
        {
            return services;
        }

        services.AddSingleton(sp =>
        {
            var embeddingProvider = sp.GetRequiredService<IEmbeddingProvider>();
            return new QdrantService(
                host: settings.Qdrant.GetHost(),
                port: settings.Qdrant.GetGrpcPort(),
                embeddingProvider: embeddingProvider,
                collectionName: settings.Qdrant.CollectionName);
        });

        return services;
    }

    /// <summary>
    /// 設定を読み込む。
    /// </summary>
    /// <param name="basePath">設定ファイルの基底パス</param>
    /// <returns>設定</returns>
    public static IConfiguration LoadConfiguration(string? basePath = null)
    {
        basePath ??= AppContext.BaseDirectory;

        var environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
                          ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                          ?? "Production";

        var builder = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddJsonFile($"appsettings.{environment}.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables();

        return builder.Build();
    }

    /// <summary>
    /// AppSettings を直接読み込む（DI なしで使用する場合）。
    /// </summary>
    /// <param name="basePath">設定ファイルの基底パス</param>
    /// <returns>AppSettings</returns>
    public static AppSettings LoadAppSettings(string? basePath = null)
    {
        var configuration = LoadConfiguration(basePath);
        var settings = new AppSettings();
        configuration.GetSection(AppSettings.SectionName).Bind(settings);
        return settings;
    }
}
