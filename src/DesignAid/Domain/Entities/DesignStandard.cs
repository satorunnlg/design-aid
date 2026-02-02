namespace DesignAid.Domain.Entities;

/// <summary>
/// 設計基準を表すエンティティ。
/// パーツに適用される設計ルール・基準を定義する。
/// </summary>
public class DesignStandard
{
    /// <summary>基準ID（例: STD-MATERIAL-01）</summary>
    public string StandardId { get; private set; } = string.Empty;

    /// <summary>基準名</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>説明</summary>
    public string? Description { get; set; }

    /// <summary>バリデーションルール（JSON形式）</summary>
    public string? ValidationRuleJson { get; set; }

    /// <summary>
    /// EF Core 用のパラメータなしコンストラクタ。
    /// </summary>
    protected DesignStandard() { }

    /// <summary>
    /// 新しい設計基準を生成する。
    /// </summary>
    /// <param name="standardId">基準ID</param>
    /// <param name="name">基準名</param>
    /// <param name="description">説明</param>
    /// <param name="validationRuleJson">バリデーションルールJSON</param>
    /// <returns>DesignStandard インスタンス</returns>
    public static DesignStandard Create(
        string standardId,
        string name,
        string? description = null,
        string? validationRuleJson = null)
    {
        if (string.IsNullOrWhiteSpace(standardId))
            throw new ArgumentException("基準IDは必須です", nameof(standardId));

        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("基準名は必須です", nameof(name));

        return new DesignStandard
        {
            StandardId = standardId,
            Name = name,
            Description = description,
            ValidationRuleJson = validationRuleJson
        };
    }

    /// <summary>
    /// 既存の設計基準を再構築する（DB から読み込み時）。
    /// </summary>
    public static DesignStandard Reconstruct(
        string standardId,
        string name,
        string? description = null,
        string? validationRuleJson = null)
    {
        return new DesignStandard
        {
            StandardId = standardId,
            Name = name,
            Description = description,
            ValidationRuleJson = validationRuleJson
        };
    }

    /// <summary>
    /// 基準情報を更新する。
    /// </summary>
    public void Update(
        string? name = null,
        string? description = null,
        string? validationRuleJson = null)
    {
        if (!string.IsNullOrWhiteSpace(name))
            Name = name;

        Description = description ?? Description;
        ValidationRuleJson = validationRuleJson ?? ValidationRuleJson;
    }
}
