using DesignAid.Infrastructure.Persistence;

namespace DesignAid.Application.Services;

/// <summary>
/// 設定管理サービスのインターフェース。
/// </summary>
public interface ISettingsService
{
    /// <summary>
    /// DB から全設定をロードする。
    /// </summary>
    Task LoadAsync(DesignAidDbContext context);

    /// <summary>
    /// 文字列値を取得する。
    /// </summary>
    string? Get(string key, string? defaultValue = null);

    /// <summary>
    /// bool 値を取得する。
    /// </summary>
    bool GetBool(string key, bool defaultValue = false);

    /// <summary>
    /// int 値を取得する。
    /// </summary>
    int GetInt(string key, int defaultValue = 0);

    /// <summary>
    /// 設定値を DB に保存する。
    /// </summary>
    Task SetAsync(DesignAidDbContext context, string key, string value);

    /// <summary>
    /// デフォルト値を DB に書き込む。
    /// </summary>
    Task SetDefaultsAsync(DesignAidDbContext context);

    /// <summary>
    /// 全設定を Dictionary で返す。
    /// </summary>
    Dictionary<string, string?> GetAll();

    /// <summary>
    /// 全設定を非同期で取得する。
    /// </summary>
    Task<Dictionary<string, string?>> GetAllAsync(DesignAidDbContext context);

    /// <summary>
    /// config.json から DB に設定を移行する。
    /// </summary>
    Task MigrateFromConfigJsonAsync(DesignAidDbContext context, string configJsonPath);
}
