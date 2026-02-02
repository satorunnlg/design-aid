namespace DesignAid.Domain.Entities;

/// <summary>
/// 装置を表すエンティティ。
/// プロジェクト内のユニット・装置単位で管理し、複数の部品を含む。
/// </summary>
public class Asset
{
    /// <summary>内部ID（UUID v4）</summary>
    public Guid Id { get; private set; }

    /// <summary>所属プロジェクトID</summary>
    public Guid ProjectId { get; private set; }

    /// <summary>装置名（ディレクトリ名、プロジェクト内でユニーク）</summary>
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

    /// <summary>所属プロジェクト（ナビゲーションプロパティ）</summary>
    public Project? Project { get; private set; }

    /// <summary>部品との関連（多対多、中間テーブル経由）</summary>
    public ICollection<AssetComponent> AssetComponents { get; private set; } = new List<AssetComponent>();

    /// <summary>
    /// EF Core 用のパラメータなしコンストラクタ。
    /// </summary>
    protected Asset() { }

    /// <summary>
    /// 新しい装置を生成する。
    /// </summary>
    /// <param name="projectId">プロジェクトID</param>
    /// <param name="name">装置名</param>
    /// <param name="directoryPath">ディレクトリパス</param>
    /// <param name="displayName">表示名</param>
    /// <param name="description">説明</param>
    /// <returns>Asset インスタンス</returns>
    public static Asset Create(
        Guid projectId,
        string name,
        string directoryPath,
        string? displayName = null,
        string? description = null)
    {
        if (projectId == Guid.Empty)
            throw new ArgumentException("プロジェクトIDは必須です", nameof(projectId));

        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("装置名は必須です", nameof(name));

        if (string.IsNullOrWhiteSpace(directoryPath))
            throw new ArgumentException("ディレクトリパスは必須です", nameof(directoryPath));

        var now = DateTime.UtcNow;

        return new Asset
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
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
        Guid projectId,
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
            ProjectId = projectId,
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

    /// <summary>
    /// components ディレクトリのパスを取得する。
    /// </summary>
    public string GetComponentsDirectoryPath() => Path.Combine(DirectoryPath, "components");
}
