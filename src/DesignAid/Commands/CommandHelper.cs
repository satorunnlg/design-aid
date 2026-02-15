using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using DesignAid.Application.Services;
using DesignAid.Configuration;
using DesignAid.Infrastructure.Persistence;

namespace DesignAid.Commands;

/// <summary>
/// コマンド共通のヘルパーメソッド。
/// </summary>
public static class CommandHelper
{
    /// <summary>
    /// DB ファイル名（固定）。
    /// </summary>
    public const string DatabaseFileName = "design_aid.db";

    /// <summary>
    /// プロジェクトルートから上方向に探索する最大階層数。
    /// </summary>
    private const int MaxSearchDepth = 2;

    /// <summary>
    /// 装置ディレクトリのパスを取得する。
    /// 事前に EnsureDataDirectory() で検証済みであること。
    /// </summary>
    public static string GetAssetsDirectory()
    {
        return Path.Combine(GetDataDirectory()!, "assets");
    }

    /// <summary>
    /// コンポーネントディレクトリのパスを取得する。
    /// 事前に EnsureDataDirectory() で検証済みであること。
    /// </summary>
    public static string GetComponentsDirectory()
    {
        return Path.Combine(GetDataDirectory()!, "components");
    }

    /// <summary>
    /// データディレクトリ（プロジェクトルート）のパスを取得する。
    /// 解決順序:
    ///   1. 環境変数 DA_DATA_DIR（後方互換）
    ///   2. カレントディレクトリから上方向に最大2階層まで design_aid.db を探索
    /// 見つからない場合は null を返す。
    /// </summary>
    public static string? GetDataDirectory()
    {
        var dataDir = Environment.GetEnvironmentVariable("DA_DATA_DIR");
        if (!string.IsNullOrEmpty(dataDir))
            return dataDir;

        return FindProjectRoot();
    }

    /// <summary>
    /// データディレクトリを取得し、見つからない場合はエラーメッセージを表示して exit code を設定する。
    /// コマンドから呼び出す場合はこのメソッドを使用する。
    /// </summary>
    /// <returns>データディレクトリのパス。見つからない場合は null（呼び出し元は return すること）。</returns>
    public static string? EnsureDataDirectory()
    {
        var dataDir = GetDataDirectory();
        if (dataDir == null)
        {
            Console.Error.WriteLine("[ERROR] プロジェクトが見つかりません。");
            Console.Error.WriteLine("  カレントディレクトリから上方向に design_aid.db を探しましたが見つかりませんでした。");
            Console.Error.WriteLine("  対処: プロジェクトディレクトリ内で実行するか、`daid setup` で初期化してください。");
            Environment.ExitCode = 3;
            return null;
        }
        return dataDir;
    }

    /// <summary>
    /// カレントディレクトリから上方向に最大2階層まで design_aid.db を探索し、
    /// プロジェクトルート（design_aid.db が存在するディレクトリ）を返す。
    /// 見つからない場合は null。
    /// </summary>
    public static string? FindProjectRoot()
    {
        var currentDir = Directory.GetCurrentDirectory();
        var dir = currentDir;

        for (var depth = 0; depth <= MaxSearchDepth && dir != null; depth++)
        {
            if (File.Exists(Path.Combine(dir, DatabaseFileName)))
                return dir;

            dir = Directory.GetParent(dir)?.FullName;
        }

        return null;
    }

    /// <summary>
    /// データベースファイルのパスを取得する。
    /// DB ファイル名は design_aid.db 固定。
    /// </summary>
    public static string GetDatabasePath()
    {
        return Path.Combine(GetDataDirectory()!, DatabaseFileName);
    }

    /// <summary>
    /// アーカイブディレクトリのパスを取得する。
    /// </summary>
    public static string GetArchiveDirectory()
    {
        return Path.Combine(GetDataDirectory()!, "archive");
    }

    /// <summary>
    /// アーカイブインデックスファイルのパスを取得する。
    /// </summary>
    public static string GetArchiveIndexPath()
    {
        return Path.Combine(GetDataDirectory()!, "archive_index.json");
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

    /// <summary>
    /// DI コンテナを構築して ServiceProvider を返す。
    /// Dashboard や将来の GUI から利用する。
    /// </summary>
    public static ServiceProvider BuildServiceProvider()
    {
        var dataDir = GetDataDirectory()!;
        var dbPath = GetDatabasePath();

        var services = new ServiceCollection();
        services.AddDesignAidServices(dbPath, dataDir);
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// ダッシュボード PID ファイルのパスを取得する。
    /// </summary>
    public static string GetDashboardPidPath()
    {
        return Path.Combine(GetDataDirectory()!, ".dashboard.pid");
    }
}
