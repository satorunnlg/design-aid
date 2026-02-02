namespace DesignAid.Domain.Entities;

/// <summary>
/// プロジェクトを表すエンティティ。
/// 案件・プロジェクト単位で管理し、複数の装置を含む。
/// </summary>
public class Project
{
    /// <summary>内部ID（UUID v4）</summary>
    public Guid Id { get; private set; }

    /// <summary>プロジェクト名（ディレクトリ名、ユニーク）</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>表示名</summary>
    public string? DisplayName { get; set; }

    /// <summary>説明</summary>
    public string? Description { get; set; }

    /// <summary>プロジェクトディレクトリの絶対パス</summary>
    public string DirectoryPath { get; private set; } = string.Empty;

    /// <summary>作成日時</summary>
    public DateTime CreatedAt { get; private set; }

    /// <summary>更新日時</summary>
    public DateTime UpdatedAt { get; private set; }

    /// <summary>所属する装置のリスト</summary>
    public ICollection<Asset> Assets { get; private set; } = new List<Asset>();

    /// <summary>
    /// EF Core 用のパラメータなしコンストラクタ。
    /// </summary>
    protected Project() { }

    /// <summary>
    /// 新しいプロジェクトを生成する。
    /// </summary>
    /// <param name="name">プロジェクト名</param>
    /// <param name="directoryPath">ディレクトリパス</param>
    /// <param name="displayName">表示名</param>
    /// <param name="description">説明</param>
    /// <returns>Project インスタンス</returns>
    public static Project Create(
        string name,
        string directoryPath,
        string? displayName = null,
        string? description = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("プロジェクト名は必須です", nameof(name));

        if (string.IsNullOrWhiteSpace(directoryPath))
            throw new ArgumentException("ディレクトリパスは必須です", nameof(directoryPath));

        var now = DateTime.UtcNow;

        return new Project
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
    /// 既存のプロジェクトを再構築する（DB から読み込み時）。
    /// </summary>
    public static Project Reconstruct(
        Guid id,
        string name,
        string directoryPath,
        DateTime createdAt,
        DateTime updatedAt,
        string? displayName = null,
        string? description = null)
    {
        return new Project
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
    /// プロジェクト情報を更新する。
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
    /// 装置を追加する。
    /// </summary>
    public Asset AddAsset(string name, string? displayName = null, string? description = null)
    {
        var assetPath = Path.Combine(DirectoryPath, "assets", name);
        var asset = Asset.Create(Id, name, assetPath, displayName, description);
        Assets.Add(asset);
        UpdatedAt = DateTime.UtcNow;
        return asset;
    }

    /// <summary>
    /// .da-project マーカーファイルのパスを取得する。
    /// </summary>
    public string GetMarkerFilePath() => Path.Combine(DirectoryPath, ".da-project");

    /// <summary>
    /// assets ディレクトリのパスを取得する。
    /// </summary>
    public string GetAssetsDirectoryPath() => Path.Combine(DirectoryPath, "assets");
}
