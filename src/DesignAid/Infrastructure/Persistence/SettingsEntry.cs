namespace DesignAid.Infrastructure.Persistence;

/// <summary>
/// 設定値エンティティ。Settings テーブルに永続化される。
/// キーは dot-notation（例: "embedding.provider", "backup.s3_bucket"）。
/// </summary>
public class SettingsEntry
{
    /// <summary>
    /// 設定キー（主キー）。dot-notation 形式。
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// 設定値（文字列）。
    /// </summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>
    /// 最終更新日時（UTC）。
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
