using DesignAid.Infrastructure.FileSystem;

namespace DesignAid.Commands;

/// <summary>
/// コマンド共通のヘルパーメソッド。
/// </summary>
public static class CommandHelper
{
    /// <summary>
    /// デフォルトのプロジェクトディレクトリパスを取得する。
    /// </summary>
    public static string GetDefaultProjectsPath()
    {
        var dataDir = Environment.GetEnvironmentVariable("DA_DATA_DIR");
        if (!string.IsNullOrEmpty(dataDir))
            return Path.Combine(dataDir, "projects");

        var currentDir = Directory.GetCurrentDirectory();
        var dir = currentDir;
        while (dir != null)
        {
            if (IsRepositoryRoot(dir))
                return Path.Combine(dir, "data", "projects");
            dir = Directory.GetParent(dir)?.FullName;
        }
        return Path.Combine(currentDir, "data", "projects");
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
    /// カレントディレクトリから親方向に .da-project マーカーを検索し、
    /// プロジェクトディレクトリを返す。
    /// </summary>
    public static string? FindProjectContext()
    {
        var projectMarkerService = new ProjectMarkerService();
        var dir = Directory.GetCurrentDirectory();

        while (dir != null)
        {
            if (projectMarkerService.Exists(dir))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }
        return null;
    }

    /// <summary>
    /// プロジェクトパスを解決する。
    /// </summary>
    /// <param name="explicitProject">明示的に指定されたパス（null可）</param>
    /// <returns>解決されたパスとエラーメッセージのタプル</returns>
    public static (string? path, string? error) ResolveProjectPath(string? explicitProject)
    {
        if (!string.IsNullOrEmpty(explicitProject))
        {
            var fullPath = Path.GetFullPath(explicitProject);
            var projectMarkerService = new ProjectMarkerService();
            if (!projectMarkerService.Exists(fullPath))
                return (null, $"プロジェクトが見つかりません: {fullPath}");
            return (fullPath, null);
        }

        var detected = FindProjectContext();
        if (detected == null)
        {
            return (null, "プロジェクトディレクトリ内で実行するか、--project オプションを指定してください");
        }
        return (detected, null);
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
