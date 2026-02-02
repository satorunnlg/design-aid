namespace DesignAid.Domain.ValueObjects;

/// <summary>
/// 手配ステータスを表す列挙型。
/// パーツの手配プロセスにおける状態を管理する。
/// </summary>
public enum HandoverStatus
{
    /// <summary>設計中（未手配）</summary>
    Draft = 0,

    /// <summary>手配済み</summary>
    Ordered = 1,

    /// <summary>納品済み</summary>
    Delivered = 2,

    /// <summary>キャンセル</summary>
    Canceled = 3
}

/// <summary>
/// HandoverStatus の拡張メソッド。
/// </summary>
public static class HandoverStatusExtensions
{
    /// <summary>
    /// ステータスの表示名を取得する。
    /// </summary>
    public static string ToDisplayName(this HandoverStatus status) => status switch
    {
        HandoverStatus.Draft => "設計中",
        HandoverStatus.Ordered => "手配済み",
        HandoverStatus.Delivered => "納品済み",
        HandoverStatus.Canceled => "キャンセル",
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };

    /// <summary>
    /// 文字列から HandoverStatus を解析する。
    /// </summary>
    /// <param name="value">ステータス文字列</param>
    /// <returns>対応する HandoverStatus</returns>
    /// <exception cref="ArgumentException">不正な値の場合</exception>
    public static HandoverStatus ParseStatus(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("ステータスは必須です", nameof(value));

        return value.ToLowerInvariant() switch
        {
            "draft" or "設計中" => HandoverStatus.Draft,
            "ordered" or "手配済み" => HandoverStatus.Ordered,
            "delivered" or "納品済み" => HandoverStatus.Delivered,
            "canceled" or "cancelled" or "キャンセル" => HandoverStatus.Canceled,
            _ => throw new ArgumentException($"不正なステータス: {value}", nameof(value))
        };
    }

    /// <summary>
    /// 文字列から HandoverStatus への解析を試みる。
    /// </summary>
    /// <param name="value">ステータス文字列</param>
    /// <param name="result">成功時の HandoverStatus</param>
    /// <returns>成功した場合は true</returns>
    public static bool TryParseStatus(string? value, out HandoverStatus result)
    {
        result = HandoverStatus.Draft;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        try
        {
            result = ParseStatus(value);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// ステータスが変更可能（未確定）かどうかを判定する。
    /// </summary>
    public static bool IsModifiable(this HandoverStatus status) =>
        status == HandoverStatus.Draft;

    /// <summary>
    /// ステータスが確定済みかどうかを判定する。
    /// </summary>
    public static bool IsCommitted(this HandoverStatus status) =>
        status == HandoverStatus.Ordered || status == HandoverStatus.Delivered;

    /// <summary>
    /// 次の有効なステータス遷移先を取得する。
    /// </summary>
    public static IEnumerable<HandoverStatus> GetValidTransitions(this HandoverStatus status) => status switch
    {
        HandoverStatus.Draft => [HandoverStatus.Ordered, HandoverStatus.Canceled],
        HandoverStatus.Ordered => [HandoverStatus.Delivered, HandoverStatus.Canceled],
        HandoverStatus.Delivered => [],
        HandoverStatus.Canceled => [],
        _ => []
    };
}
