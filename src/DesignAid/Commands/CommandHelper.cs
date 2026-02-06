using System.Text.Json;

namespace DesignAid.Commands;

/// <summary>
/// コマンド共通のヘルパーメソッド。
/// </summary>
public static class CommandHelper
{
    /// <summary>
    /// 装置ディレクトリのパスを取得する。
    /// </summary>
    public static string GetAssetsDirectory()
    {
        var dataDir = Environment.GetEnvironmentVariable("DA_DATA_DIR");
        if (!string.IsNullOrEmpty(dataDir))
            return Path.Combine(dataDir, "assets");

        var currentDir = Directory.GetCurrentDirectory();
        var dir = currentDir;
        while (dir != null)
        {
            if (IsRepositoryRoot(dir))
                return Path.Combine(dir, "data", "assets");
            dir = Directory.GetParent(dir)?.FullName;
        }
        return Path.Combine(currentDir, "data", "assets");
    }

    /// <summary>
    /// コンポーネントディレクトリのパスを取得する。
    /// </summary>
    public static string GetComponentsDirectory()
    {
        var dataDir = Environment.GetEnvironmentVariable("DA_DATA_DIR");
        if (!string.IsNullOrEmpty(dataDir))
            return Path.Combine(dataDir, "components");

        var currentDir = Directory.GetCurrentDirectory();
        var dir = currentDir;
        while (dir != null)
        {
            if (IsRepositoryRoot(dir))
                return Path.Combine(dir, "data", "components");
            dir = Directory.GetParent(dir)?.FullName;
        }
        return Path.Combine(currentDir, "data", "components");
    }

    /// <summary>
    /// データディレクトリのパスを取得する。
    /// </summary>
    public static string GetDataDirectory()
    {
        var dataDir = Environment.GetEnvironmentVariable("DA_DATA_DIR");
        if (!string.IsNullOrEmpty(dataDir))
            return dataDir;

        var currentDir = Directory.GetCurrentDirectory();
        var dir = currentDir;
        while (dir != null)
        {
            if (IsRepositoryRoot(dir))
                return Path.Combine(dir, "data");
            dir = Directory.GetParent(dir)?.FullName;
        }
        return Path.Combine(currentDir, "data");
    }

    /// <summary>
    /// リポジトリルートかどうかを判定する。
    /// </summary>
    public static bool IsRepositoryRoot(string dir)
    {
        return File.Exists(Path.Combine(dir, "DesignAid.sln"))
            || File.Exists(Path.Combine(dir, "DesignAid.slnx"));
    }

    /// <summary>
    /// データベースファイルのパスを取得する。
    /// </summary>
    public static string GetDatabasePath()
    {
        var dbPath = Environment.GetEnvironmentVariable("DA_DB_PATH");
        if (!string.IsNullOrEmpty(dbPath))
            return dbPath;

        return Path.Combine(GetDataDirectory(), "design_aid.db");
    }

    /// <summary>
    /// アーカイブディレクトリのパスを取得する。
    /// </summary>
    public static string GetArchiveDirectory()
    {
        return Path.Combine(GetDataDirectory(), "archive");
    }

    /// <summary>
    /// アーカイブインデックスファイルのパスを取得する。
    /// </summary>
    public static string GetArchiveIndexPath()
    {
        return Path.Combine(GetDataDirectory(), "archive_index.json");
    }

    /// <summary>
    /// config.json から Qdrant 設定を取得する。
    /// 環境変数でホスト・ポートをオーバーライド可能。
    /// </summary>
    public static (string host, int port, string collectionName) GetQdrantConfig()
    {
        var host = Environment.GetEnvironmentVariable("DA_QDRANT_HOST") ?? "localhost";
        var portStr = Environment.GetEnvironmentVariable("DA_QDRANT_GRPC_PORT")
                      ?? Environment.GetEnvironmentVariable("DA_QDRANT_PORT");
        var port = int.TryParse(portStr, out var p) ? p : 6334;
        var collectionName = "design_knowledge";

        // config.json からコレクション名を読み取る
        var configPath = Path.Combine(GetDataDirectory(), "config.json");
        if (File.Exists(configPath))
        {
            try
            {
                var json = File.ReadAllText(configPath);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("qdrant", out var qdrant))
                {
                    if (qdrant.TryGetProperty("collection_name", out var cn))
                        collectionName = cn.GetString() ?? collectionName;

                    // config.json のホスト・ポートも環境変数がなければ使用
                    if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DA_QDRANT_HOST"))
                        && qdrant.TryGetProperty("host", out var h))
                        host = h.GetString() ?? host;

                    if (portStr == null && qdrant.TryGetProperty("grpc_port", out var gp))
                        port = gp.GetInt32();
                }
            }
            catch
            {
                // config.json の読み取り失敗時はデフォルト値を使用
            }
        }

        return (host, port, collectionName);
    }
}
