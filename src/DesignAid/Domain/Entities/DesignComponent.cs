using DesignAid.Domain.ValueObjects;

namespace DesignAid.Domain.Entities;

/// <summary>
/// パーツの種別を表す列挙型。
/// </summary>
public enum PartType
{
    /// <summary>製作物（図面品）</summary>
    Fabricated = 0,

    /// <summary>購入品</summary>
    Purchased = 1,

    /// <summary>規格品</summary>
    Standard = 2
}

/// <summary>
/// 全てのパーツの基底クラス。
/// 手配境界を越えるために必要な最小限の情報を保持する。
/// 部品は共有リソースとして管理され、複数の装置から参照可能。
/// </summary>
public abstract class DesignComponent
{
    /// <summary>内部ID（UUID v4、データ管理用）</summary>
    public Guid Id { get; protected set; }

    /// <summary>型式（人間が識別する番号、例: SP-2026-PLATE-01）グローバルユニーク</summary>
    public PartNumber PartNumber { get; protected set; }

    /// <summary>パーツ名</summary>
    public string Name { get; protected set; } = string.Empty;

    /// <summary>パーツ種別</summary>
    public abstract PartType Type { get; }

    /// <summary>バージョン（セマンティックバージョニング）</summary>
    public string Version { get; protected set; } = "1.0.0";

    /// <summary>成果物ファイルパスとハッシュ値のマップ</summary>
    public Dictionary<string, FileHash> ArtifactHashes { get; protected set; } = new();

    /// <summary>現在のハッシュ（全成果物の結合ハッシュ）</summary>
    public string CurrentHash { get; protected set; } = string.Empty;

    /// <summary>手配ステータス</summary>
    public HandoverStatus Status { get; protected set; } = HandoverStatus.Draft;

    /// <summary>パーツディレクトリのパス（data/components/xxx）</summary>
    public string DirectoryPath { get; protected set; } = string.Empty;

    /// <summary>メタデータ（JSON形式で保存）</summary>
    public Dictionary<string, object> Metadata { get; protected set; } = new();

    /// <summary>メモ</summary>
    public string? Memo { get; set; }

    /// <summary>作成日時</summary>
    public DateTime CreatedAt { get; protected set; }

    /// <summary>更新日時</summary>
    public DateTime UpdatedAt { get; protected set; }

    /// <summary>装置との関連（多対多、ナビゲーションプロパティ）</summary>
    public ICollection<AssetComponent> AssetComponents { get; protected set; } = new List<AssetComponent>();

    /// <summary>適用設計基準のIDリスト</summary>
    public List<string> StandardIds { get; protected set; } = new();

    /// <summary>
    /// EF Core 用のパラメータなしコンストラクタ。
    /// </summary>
    protected DesignComponent() { }

    /// <summary>
    /// 成果物のハッシュを計算し、整合性を検証する。
    /// </summary>
    public abstract ValidationResult ValidateIntegrity();

    /// <summary>
    /// ステータスを変更する。
    /// </summary>
    /// <param name="newStatus">新しいステータス</param>
    /// <exception cref="InvalidOperationException">無効な遷移の場合</exception>
    public void ChangeStatus(HandoverStatus newStatus)
    {
        var validTransitions = Status.GetValidTransitions();
        if (!validTransitions.Contains(newStatus))
        {
            throw new InvalidOperationException(
                $"ステータス '{Status.ToDisplayName()}' から '{newStatus.ToDisplayName()}' への遷移は無効です");
        }

        Status = newStatus;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// バージョンを更新する。
    /// </summary>
    public void UpdateVersion(string newVersion)
    {
        if (string.IsNullOrWhiteSpace(newVersion))
            throw new ArgumentException("バージョンは必須です", nameof(newVersion));

        Version = newVersion;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// 成果物のハッシュを更新する。
    /// </summary>
    public void UpdateArtifactHash(string relativePath, FileHash hash)
    {
        ArtifactHashes[relativePath] = hash;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// 現在の結合ハッシュを更新する。
    /// </summary>
    public void UpdateCurrentHash(string hash)
    {
        CurrentHash = hash;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// メタデータを更新する。
    /// </summary>
    public void SetMetadata(string key, object value)
    {
        Metadata[key] = value;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// part.json ファイルのパスを取得する。
    /// </summary>
    public string GetPartJsonPath() => Path.Combine(DirectoryPath, "part.json");

    /// <summary>
    /// 変更が可能かどうかを判定する。
    /// </summary>
    public bool IsModifiable() => Status.IsModifiable();
}
