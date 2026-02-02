using DesignAid.Domain.ValueObjects;

namespace DesignAid.Domain.Entities;

/// <summary>
/// 手配履歴を表すエンティティ。
/// パーツの手配（発注）ごとの履歴を管理する。
/// </summary>
public class HandoverRecord
{
    /// <summary>履歴ID（自動採番）</summary>
    public int Id { get; private set; }

    /// <summary>パーツID</summary>
    public Guid PartId { get; private set; }

    /// <summary>手配時のハッシュ（コミットハッシュ）</summary>
    public string CommittedHash { get; private set; } = string.Empty;

    /// <summary>手配ステータス</summary>
    public HandoverStatus Status { get; private set; }

    /// <summary>発注日</summary>
    public DateTime OrderDate { get; private set; }

    /// <summary>納品日</summary>
    public DateTime? DeliveryDate { get; private set; }

    /// <summary>備考</summary>
    public string? Notes { get; set; }

    /// <summary>対象パーツ（ナビゲーションプロパティ）</summary>
    public DesignComponent? Part { get; private set; }

    /// <summary>
    /// EF Core 用のパラメータなしコンストラクタ。
    /// </summary>
    protected HandoverRecord() { }

    /// <summary>
    /// 新しい手配履歴を生成する（発注時）。
    /// </summary>
    /// <param name="partId">パーツID</param>
    /// <param name="committedHash">手配時のハッシュ</param>
    /// <param name="notes">備考</param>
    /// <returns>HandoverRecord インスタンス</returns>
    public static HandoverRecord CreateOrder(
        Guid partId,
        string committedHash,
        string? notes = null)
    {
        if (partId == Guid.Empty)
            throw new ArgumentException("パーツIDは必須です", nameof(partId));

        if (string.IsNullOrWhiteSpace(committedHash))
            throw new ArgumentException("コミットハッシュは必須です", nameof(committedHash));

        return new HandoverRecord
        {
            PartId = partId,
            CommittedHash = committedHash,
            Status = HandoverStatus.Ordered,
            OrderDate = DateTime.UtcNow,
            Notes = notes
        };
    }

    /// <summary>
    /// 既存の手配履歴を再構築する（DB から読み込み時）。
    /// </summary>
    public static HandoverRecord Reconstruct(
        int id,
        Guid partId,
        string committedHash,
        HandoverStatus status,
        DateTime orderDate,
        DateTime? deliveryDate = null,
        string? notes = null)
    {
        return new HandoverRecord
        {
            Id = id,
            PartId = partId,
            CommittedHash = committedHash,
            Status = status,
            OrderDate = orderDate,
            DeliveryDate = deliveryDate,
            Notes = notes
        };
    }

    /// <summary>
    /// 納品を記録する。
    /// </summary>
    /// <param name="deliveryDate">納品日（省略時は現在日時）</param>
    /// <param name="notes">追加備考</param>
    public void MarkAsDelivered(DateTime? deliveryDate = null, string? notes = null)
    {
        if (Status != HandoverStatus.Ordered)
        {
            throw new InvalidOperationException(
                $"ステータス '{Status.ToDisplayName()}' の手配履歴は納品済みにできません");
        }

        Status = HandoverStatus.Delivered;
        DeliveryDate = deliveryDate ?? DateTime.UtcNow;

        if (!string.IsNullOrEmpty(notes))
        {
            Notes = string.IsNullOrEmpty(Notes)
                ? notes
                : $"{Notes}\n{notes}";
        }
    }

    /// <summary>
    /// キャンセルを記録する。
    /// </summary>
    /// <param name="reason">キャンセル理由</param>
    public void Cancel(string? reason = null)
    {
        if (Status == HandoverStatus.Delivered)
        {
            throw new InvalidOperationException("納品済みの手配履歴はキャンセルできません");
        }

        if (Status == HandoverStatus.Canceled)
        {
            throw new InvalidOperationException("既にキャンセル済みです");
        }

        Status = HandoverStatus.Canceled;

        if (!string.IsNullOrEmpty(reason))
        {
            Notes = string.IsNullOrEmpty(Notes)
                ? $"キャンセル理由: {reason}"
                : $"{Notes}\nキャンセル理由: {reason}";
        }
    }

    /// <summary>
    /// リードタイムを計算する（発注から納品までの日数）。
    /// </summary>
    /// <returns>リードタイム（日）。未納品の場合は null</returns>
    public int? CalculateLeadTimeDays()
    {
        if (DeliveryDate == null)
            return null;

        return (int)(DeliveryDate.Value - OrderDate).TotalDays;
    }
}
