namespace DesignAid.Domain.Entities;

/// <summary>
/// 装置を表すエンティティ。
/// トップレベルのユニット・装置単位で管理し、複数の部品や子装置を含む。
/// </summary>
public class Asset
{
    /// <summary>内部ID（UUID v4）</summary>
    public Guid Id { get; private set; }

    /// <summary>装置名（ディレクトリ名、グローバルでユニーク）</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>表示名</summary>
    public string? DisplayName { get; set; }

    /// <summary>説明</summary>
    public string? Description { get; set; }

    /// <summary>装置ディレクトリの絶対パス</summary>
    public string DirectoryPath { get; private set; } = string.Empty;

    /// <summary>作成日時</summary>
    public DateTime CreatedAt { get; private set; }

    /// <summary>更新日時</summary>
    public DateTime UpdatedAt { get; private set; }

    /// <summary>部品との関連（多対多、中間テーブル経由）</summary>
    public ICollection<AssetComponent> AssetComponents { get; private set; } = new List<AssetComponent>();

    /// <summary>子装置との関連（この装置が親の場合）</summary>
    public ICollection<AssetSubAsset> ChildAssets { get; private set; } = new List<AssetSubAsset>();

    /// <summary>親装置との関連（この装置が子の場合）</summary>
    public ICollection<AssetSubAsset> ParentAssets { get; private set; } = new List<AssetSubAsset>();

    /// <summary>
    /// EF Core 用のパラメータなしコンストラクタ。
    /// </summary>
    protected Asset() { }

    /// <summary>
    /// 新しい装置を生成する。
    /// </summary>
    /// <param name="name">装置名</param>
    /// <param name="directoryPath">ディレクトリパス</param>
    /// <param name="displayName">表示名</param>
    /// <param name="description">説明</param>
    /// <returns>Asset インスタンス</returns>
    public static Asset Create(
        string name,
        string directoryPath,
        string? displayName = null,
        string? description = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("装置名は必須です", nameof(name));

        if (string.IsNullOrWhiteSpace(directoryPath))
            throw new ArgumentException("ディレクトリパスは必須です", nameof(directoryPath));

        var now = DateTime.UtcNow;

        return new Asset
        {
            Id = Guid.NewGuid(),
            Name = name,
            DisplayName = displayName,
            Description = description,
            DirectoryPath = Path.GetFullPath(directoryPath),
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    /// <summary>
    /// 既存の装置を再構築する（DB から読み込み時）。
    /// </summary>
    public static Asset Reconstruct(
        Guid id,
        string name,
        string directoryPath,
        DateTime createdAt,
        DateTime updatedAt,
        string? displayName = null,
        string? description = null)
    {
        return new Asset
        {
            Id = id,
            Name = name,
            DisplayName = displayName,
            Description = description,
            DirectoryPath = directoryPath,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt
        };
    }

    /// <summary>
    /// 装置情報を更新する。
    /// </summary>
    public void Update(string? displayName = null, string? description = null)
    {
        DisplayName = displayName ?? DisplayName;
        Description = description ?? Description;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// ディレクトリパスを更新する（移動時）。
    /// </summary>
    public void UpdatePath(string newDirectoryPath)
    {
        if (string.IsNullOrWhiteSpace(newDirectoryPath))
            throw new ArgumentException("ディレクトリパスは必須です", nameof(newDirectoryPath));

        DirectoryPath = Path.GetFullPath(newDirectoryPath);
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// asset.json ファイルのパスを取得する。
    /// </summary>
    public string GetAssetJsonPath() => Path.Combine(DirectoryPath, "asset.json");
}

/// <summary>
/// 装置-子装置の関連を表す中間エンティティ。
/// </summary>
public class AssetSubAsset
{
    /// <summary>親装置ID</summary>
    public Guid ParentAssetId { get; set; }

    /// <summary>子装置ID</summary>
    public Guid ChildAssetId { get; set; }

    /// <summary>使用数量</summary>
    public int Quantity { get; set; } = 1;

    /// <summary>備考</summary>
    public string? Notes { get; set; }

    /// <summary>作成日時</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>親装置（ナビゲーションプロパティ）</summary>
    public Asset? ParentAsset { get; set; }

    /// <summary>子装置（ナビゲーションプロパティ）</summary>
    public Asset? ChildAsset { get; set; }
}
