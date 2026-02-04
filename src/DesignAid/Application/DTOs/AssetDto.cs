namespace DesignAid.Application.DTOs;

/// <summary>
/// 装置情報のDTO。
/// </summary>
public class AssetDto
{
    /// <summary>装置ID（UUID）</summary>
    public Guid Id { get; set; }

    /// <summary>プロジェクトID</summary>
    public Guid ProjectId { get; set; }

    /// <summary>プロジェクト名</summary>
    public string? ProjectName { get; set; }

    /// <summary>装置名</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>表示名</summary>
    public string? DisplayName { get; set; }

    /// <summary>説明</summary>
    public string? Description { get; set; }

    /// <summary>ディレクトリパス</summary>
    public string DirectoryPath { get; set; } = string.Empty;

    /// <summary>部品数</summary>
    public int PartCount { get; set; }

    /// <summary>作成日時</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>更新日時</summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>部品一覧（オプション）</summary>
    public List<PartSummaryDto>? Parts { get; set; }

    /// <summary>ステータス別集計</summary>
    public StatusSummary? StatusSummary { get; set; }
}

/// <summary>
/// 部品サマリー情報のDTO。
/// </summary>
public class PartSummaryDto
{
    /// <summary>部品ID</summary>
    public Guid Id { get; set; }

    /// <summary>型式</summary>
    public string PartNumber { get; set; } = string.Empty;

    /// <summary>名称</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>種別</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>ステータス</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>使用数量</summary>
    public int Quantity { get; set; }
}

/// <summary>
/// ステータス別集計。
/// </summary>
public class StatusSummary
{
    /// <summary>下書き数</summary>
    public int Draft { get; set; }

    /// <summary>手配済み数</summary>
    public int Ordered { get; set; }

    /// <summary>納品済み数</summary>
    public int Delivered { get; set; }

    /// <summary>キャンセル数</summary>
    public int Canceled { get; set; }

    /// <summary>合計</summary>
    public int Total => Draft + Ordered + Delivered + Canceled;
}

/// <summary>
/// 装置一覧のDTO。
/// </summary>
public class AssetListDto
{
    /// <summary>プロジェクト情報</summary>
    public ProjectSummaryDto? Project { get; set; }

    /// <summary>装置一覧</summary>
    public List<AssetDto> Assets { get; set; } = [];

    /// <summary>総装置数</summary>
    public int TotalAssets => Assets.Count;

    /// <summary>総部品数</summary>
    public int TotalParts => Assets.Sum(a => a.PartCount);
}

/// <summary>
/// プロジェクトサマリー情報のDTO。
/// </summary>
public class ProjectSummaryDto
{
    /// <summary>プロジェクトID</summary>
    public Guid Id { get; set; }

    /// <summary>プロジェクト名</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>表示名</summary>
    public string? DisplayName { get; set; }
}
