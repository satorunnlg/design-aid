namespace DesignAid.Application.DTOs;

/// <summary>
/// プロジェクト情報のDTO。
/// </summary>
public class ProjectDto
{
    /// <summary>プロジェクトID（UUID）</summary>
    public Guid Id { get; set; }

    /// <summary>プロジェクト名</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>表示名</summary>
    public string? DisplayName { get; set; }

    /// <summary>説明</summary>
    public string? Description { get; set; }

    /// <summary>ディレクトリパス</summary>
    public string DirectoryPath { get; set; } = string.Empty;

    /// <summary>装置数</summary>
    public int AssetCount { get; set; }

    /// <summary>部品数（装置経由の総数）</summary>
    public int PartCount { get; set; }

    /// <summary>作成日時</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>更新日時</summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>最終同期日時</summary>
    public DateTime? LastSyncAt { get; set; }

    /// <summary>装置一覧（オプション）</summary>
    public List<AssetSummaryDto>? Assets { get; set; }
}

/// <summary>
/// 装置サマリー情報のDTO。
/// </summary>
public class AssetSummaryDto
{
    /// <summary>装置ID</summary>
    public Guid Id { get; set; }

    /// <summary>装置名</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>表示名</summary>
    public string? DisplayName { get; set; }

    /// <summary>部品数</summary>
    public int PartCount { get; set; }
}

/// <summary>
/// プロジェクト一覧のDTO。
/// </summary>
public class ProjectListDto
{
    /// <summary>プロジェクト一覧</summary>
    public List<ProjectDto> Projects { get; set; } = [];

    /// <summary>総プロジェクト数</summary>
    public int TotalProjects => Projects.Count;

    /// <summary>総装置数</summary>
    public int TotalAssets => Projects.Sum(p => p.AssetCount);

    /// <summary>総部品数</summary>
    public int TotalParts => Projects.Sum(p => p.PartCount);
}
