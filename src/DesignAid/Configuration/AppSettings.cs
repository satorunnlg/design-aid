namespace DesignAid.Configuration;

/// <summary>
/// アプリケーション設定のルートクラス。
/// </summary>
public class AppSettings
{
    /// <summary>
    /// 設定セクション名。
    /// </summary>
    public const string SectionName = "DesignAid";

    /// <summary>
    /// システムディレクトリ（DB、設定の配置先）。
    /// null の場合は OS に応じたデフォルトパスを使用。
    /// </summary>
    public string? SystemDirectory { get; set; }

    /// <summary>
    /// データベース設定。
    /// </summary>
    public DatabaseSettings Database { get; set; } = new();

    /// <summary>
    /// Qdrant 設定。
    /// </summary>
    public QdrantSettings Qdrant { get; set; } = new();

    /// <summary>
    /// 埋め込み設定。
    /// </summary>
    public EmbeddingSettings Embedding { get; set; } = new();

    /// <summary>
    /// ハッシュ設定。
    /// </summary>
    public HashingSettings Hashing { get; set; } = new();

    /// <summary>
    /// システムディレクトリを取得する（環境変数や OS を考慮）。
    /// </summary>
    public string GetSystemDirectory()
    {
        // 環境変数を優先
        var envDir = Environment.GetEnvironmentVariable("DA_DATA_DIR");
        if (!string.IsNullOrEmpty(envDir))
        {
            return Path.GetFullPath(envDir);
        }

        // 設定ファイルの値
        if (!string.IsNullOrEmpty(SystemDirectory))
        {
            return Path.GetFullPath(SystemDirectory);
        }

        // OS に応じたデフォルトパス
        if (OperatingSystem.IsWindows())
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "design-aid");
        }
        else
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, ".design-aid");
        }
    }

    /// <summary>
    /// データベースファイルの完全パスを取得する。
    /// </summary>
    public string GetDatabasePath()
    {
        var envPath = Environment.GetEnvironmentVariable("DA_DB_PATH");
        if (!string.IsNullOrEmpty(envPath))
        {
            return Path.GetFullPath(envPath);
        }

        return Path.Combine(GetSystemDirectory(), Database.Path);
    }
}

/// <summary>
/// データベース設定。
/// </summary>
public class DatabaseSettings
{
    /// <summary>
    /// データベースファイル名（システムディレクトリからの相対パス）。
    /// </summary>
    public string Path { get; set; } = "design_aid.db";
}

/// <summary>
/// Qdrant 設定。
/// </summary>
public class QdrantSettings
{
    /// <summary>
    /// Qdrant ホスト。
    /// </summary>
    public string Host { get; set; } = "localhost";

    /// <summary>
    /// Qdrant REST API ポート。
    /// </summary>
    public int Port { get; set; } = 6333;

    /// <summary>
    /// Qdrant gRPC ポート（Qdrant.Client 用）。
    /// </summary>
    public int GrpcPort { get; set; } = 6334;

    /// <summary>
    /// コレクション名。
    /// </summary>
    public string CollectionName { get; set; } = "design_knowledge";

    /// <summary>
    /// Qdrant 機能の有効/無効。
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 環境変数を考慮したホストを取得する。
    /// </summary>
    public string GetHost()
    {
        return Environment.GetEnvironmentVariable("DA_QDRANT_HOST") ?? Host;
    }

    /// <summary>
    /// 環境変数を考慮した gRPC ポートを取得する。
    /// </summary>
    public int GetGrpcPort()
    {
        var portStr = Environment.GetEnvironmentVariable("DA_QDRANT_GRPC_PORT")
                      ?? Environment.GetEnvironmentVariable("DA_QDRANT_PORT");
        if (int.TryParse(portStr, out var port))
        {
            return port;
        }
        return GrpcPort;
    }

    /// <summary>
    /// 環境変数を考慮した有効/無効を取得する。
    /// </summary>
    public bool IsEnabled()
    {
        var envEnabled = Environment.GetEnvironmentVariable("DA_QDRANT_ENABLED");
        if (!string.IsNullOrEmpty(envEnabled))
        {
            return !envEnabled.Equals("false", StringComparison.OrdinalIgnoreCase);
        }
        return Enabled;
    }
}

/// <summary>
/// 埋め込み設定。
/// </summary>
public class EmbeddingSettings
{
    /// <summary>
    /// 使用するプロバイダー名。
    /// </summary>
    public string Provider { get; set; } = "Mock";

    /// <summary>
    /// プロバイダー別設定。
    /// </summary>
    public Dictionary<string, EmbeddingProviderSettings> Providers { get; set; } = new();

    /// <summary>
    /// 環境変数を考慮したプロバイダー名を取得する。
    /// </summary>
    public string GetProvider()
    {
        return Environment.GetEnvironmentVariable("DA_EMBEDDING_PROVIDER") ?? Provider;
    }

    /// <summary>
    /// 現在のプロバイダー設定を取得する。
    /// </summary>
    public EmbeddingProviderSettings? GetCurrentProviderSettings()
    {
        var provider = GetProvider();
        return Providers.GetValueOrDefault(provider);
    }
}

/// <summary>
/// 埋め込みプロバイダー別設定。
/// </summary>
public class EmbeddingProviderSettings
{
    /// <summary>
    /// モデル名。
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// ベクトル次元数。
    /// </summary>
    public int Dimensions { get; set; } = 384;

    /// <summary>
    /// ホスト URL（Ollama 用）。
    /// </summary>
    public string? Host { get; set; }

    /// <summary>
    /// エンドポイント URL（Azure 用）。
    /// </summary>
    public string? Endpoint { get; set; }

    /// <summary>
    /// デプロイメント名（Azure 用）。
    /// </summary>
    public string? DeploymentName { get; set; }
}

/// <summary>
/// ハッシュ設定。
/// </summary>
public class HashingSettings
{
    /// <summary>
    /// ハッシュアルゴリズム。
    /// </summary>
    public string Algorithm { get; set; } = "SHA256";
}
