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
}
