namespace DesignAid.Domain.Entities;

/// <summary>
/// 装置-部品の関連を表す中間エンティティ。
/// 同じ部品を複数の装置で共有可能にする。
/// </summary>
public class AssetComponent
{
    /// <summary>装置ID</summary>
    public Guid AssetId { get; private set; }

    /// <summary>部品ID</summary>
    public Guid PartId { get; private set; }

    /// <summary>使用数量</summary>
    public int Quantity { get; private set; } = 1;

    /// <summary>備考（この装置での用途など）</summary>
    public string? Notes { get; set; }

    /// <summary>作成日時</summary>
    public DateTime CreatedAt { get; private set; }

    /// <summary>装置（ナビゲーションプロパティ）</summary>
    public Asset? Asset { get; private set; }

    /// <summary>部品（ナビゲーションプロパティ）</summary>
    public DesignComponent? Part { get; private set; }

    /// <summary>
    /// EF Core 用のパラメータなしコンストラクタ。
    /// </summary>
    protected AssetComponent() { }

    /// <summary>
    /// 新しい装置-部品関連を生成する。
    /// </summary>
    /// <param name="assetId">装置ID</param>
    /// <param name="partId">部品ID</param>
    /// <param name="quantity">使用数量</param>
    /// <param name="notes">備考</param>
    /// <returns>AssetComponent インスタンス</returns>
    public static AssetComponent Create(
        Guid assetId,
        Guid partId,
        int quantity = 1,
        string? notes = null)
    {
        if (assetId == Guid.Empty)
            throw new ArgumentException("装置IDは必須です", nameof(assetId));

        if (partId == Guid.Empty)
            throw new ArgumentException("部品IDは必須です", nameof(partId));

        if (quantity < 1)
            throw new ArgumentException("数量は1以上である必要があります", nameof(quantity));

        return new AssetComponent
        {
            AssetId = assetId,
            PartId = partId,
            Quantity = quantity,
            Notes = notes,
            CreatedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// 既存の関連を再構築する（DB から読み込み時）。
    /// </summary>
    public static AssetComponent Reconstruct(
        Guid assetId,
        Guid partId,
        int quantity,
        DateTime createdAt,
        string? notes = null)
    {
        return new AssetComponent
        {
            AssetId = assetId,
            PartId = partId,
            Quantity = quantity,
            Notes = notes,
            CreatedAt = createdAt
        };
    }

    /// <summary>
    /// 数量を更新する。
    /// </summary>
    public void UpdateQuantity(int newQuantity)
    {
        if (newQuantity < 1)
            throw new ArgumentException("数量は1以上である必要があります", nameof(newQuantity));

        Quantity = newQuantity;
    }
}
