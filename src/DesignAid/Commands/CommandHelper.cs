using DesignAid.Application.Services;
using DesignAid.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

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
        return Path.Combine(GetDataDirectory(), "assets");
    }

    /// <summary>
    /// コンポーネントディレクトリのパスを取得する。
    /// </summary>
    public static string GetComponentsDirectory()
    {
        return Path.Combine(GetDataDirectory(), "components");
    }

    /// <summary>
    /// データディレクトリのパスを取得する。
    /// ブートストラップ: DA_DATA_DIR 環境変数 → リポジトリルートの data/ → カレントの data/
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
    /// DB ファイル名は design_aid.db 固定。
    /// </summary>
    public static string GetDatabasePath()
    {
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
    /// DB から SettingsService をロードして返す。
    /// DB が存在しない場合はデフォルト値のみを持つインスタンスを返す。
    /// </summary>
    public static SettingsService LoadSettings()
    {
        var dbPath = GetDatabasePath();
        var service = new SettingsService();
        if (File.Exists(dbPath))
        {
            var optionsBuilder = new DbContextOptionsBuilder<DesignAidDbContext>();
            optionsBuilder.UseSqlite($"Data Source={dbPath}");
            using var context = new DesignAidDbContext(optionsBuilder.Options);
            service.LoadAsync(context).GetAwaiter().GetResult();
        }
        return service;
    }
}
