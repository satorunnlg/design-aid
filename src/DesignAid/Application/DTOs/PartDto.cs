using DesignAid.Domain.Entities;

namespace DesignAid.Application.DTOs;

/// <summary>
/// パーツ情報のDTO。
/// </summary>
public class PartDto
{
    /// <summary>パーツID（UUID）</summary>
    public Guid Id { get; set; }

    /// <summary>型式</summary>
    public string PartNumber { get; set; } = string.Empty;

    /// <summary>名称</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>種別</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>種別（列挙値）</summary>
    public PartType PartType { get; set; }

    /// <summary>バージョン</summary>
    public string Version { get; set; } = "1.0.0";

    /// <summary>現在のハッシュ</summary>
    public string CurrentHash { get; set; } = string.Empty;

    /// <summary>ステータス</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>ディレクトリパス</summary>
    public string DirectoryPath { get; set; } = string.Empty;

    /// <summary>メモ</summary>
    public string? Memo { get; set; }

    /// <summary>作成日時</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>更新日時</summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>成果物一覧</summary>
    public List<ArtifactDto>? Artifacts { get; set; }

    /// <summary>使用装置一覧</summary>
    public List<AssetUsageDto>? UsedByAssets { get; set; }

    /// <summary>手配履歴</summary>
    public List<HandoverRecordDto>? HandoverHistory { get; set; }

    /// <summary>製作物詳細（Fabricated の場合）</summary>
    public FabricatedPartDetail? FabricatedDetail { get; set; }

    /// <summary>購入品詳細（Purchased の場合）</summary>
    public PurchasedPartDetail? PurchasedDetail { get; set; }

    /// <summary>規格品詳細（Standard の場合）</summary>
    public StandardPartDetail? StandardDetail { get; set; }
}

/// <summary>
/// 成果物情報のDTO。
/// </summary>
public class ArtifactDto
{
    /// <summary>ファイルパス（相対）</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>ハッシュ値</summary>
    public string Hash { get; set; } = string.Empty;

    /// <summary>ファイル存在フラグ</summary>
    public bool Exists { get; set; }

    /// <summary>ハッシュ一致フラグ</summary>
    public bool HashMatches { get; set; }
}

/// <summary>
/// 装置での使用情報のDTO。
/// </summary>
public class AssetUsageDto
{
    /// <summary>装置ID</summary>
    public Guid AssetId { get; set; }

    /// <summary>装置名</summary>
    public string AssetName { get; set; } = string.Empty;

    /// <summary>プロジェクトID</summary>
    public Guid ProjectId { get; set; }

    /// <summary>プロジェクト名</summary>
    public string ProjectName { get; set; } = string.Empty;

    /// <summary>使用数量</summary>
    public int Quantity { get; set; }

    /// <summary>備考</summary>
    public string? Notes { get; set; }
}

/// <summary>
/// 手配履歴のDTO。
/// </summary>
public class HandoverRecordDto
{
    /// <summary>レコードID</summary>
    public int Id { get; set; }

    /// <summary>手配時ハッシュ</summary>
    public string CommittedHash { get; set; } = string.Empty;

    /// <summary>ステータス</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>手配日</summary>
    public DateTime OrderDate { get; set; }

    /// <summary>納品日</summary>
    public DateTime? DeliveryDate { get; set; }

    /// <summary>備考</summary>
    public string? Notes { get; set; }
}

/// <summary>
/// 製作物詳細情報。
/// </summary>
public class FabricatedPartDetail
{
    /// <summary>材質</summary>
    public string? Material { get; set; }

    /// <summary>表面処理</summary>
    public string? SurfaceTreatment { get; set; }

    /// <summary>リードタイム（日）</summary>
    public int? LeadTimeDays { get; set; }
}

/// <summary>
/// 購入品詳細情報。
/// </summary>
public class PurchasedPartDetail
{
    /// <summary>メーカー</summary>
    public string? Manufacturer { get; set; }

    /// <summary>メーカー型番</summary>
    public string? ManufacturerPartNumber { get; set; }

    /// <summary>リードタイム（日）</summary>
    public int? LeadTimeDays { get; set; }
}

/// <summary>
/// 規格品詳細情報。
/// </summary>
public class StandardPartDetail
{
    /// <summary>規格番号</summary>
    public string? StandardNumber { get; set; }

    /// <summary>サイズ</summary>
    public string? Size { get; set; }

    /// <summary>材質等級</summary>
    public string? MaterialGrade { get; set; }
}

/// <summary>
/// パーツ一覧のDTO。
/// </summary>
public class PartListDto
{
    /// <summary>パーツ一覧</summary>
    public List<PartDto> Parts { get; set; } = [];

    /// <summary>総数</summary>
    public int TotalCount => Parts.Count;

    /// <summary>種別別集計</summary>
    public TypeSummary? TypeSummary { get; set; }

    /// <summary>ステータス別集計</summary>
    public StatusSummary? StatusSummary { get; set; }
}

/// <summary>
/// 種別別集計。
/// </summary>
public class TypeSummary
{
    /// <summary>製作物数</summary>
    public int Fabricated { get; set; }

    /// <summary>購入品数</summary>
    public int Purchased { get; set; }

    /// <summary>規格品数</summary>
    public int Standard { get; set; }

    /// <summary>合計</summary>
    public int Total => Fabricated + Purchased + Standard;
}
