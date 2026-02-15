namespace DesignAid.Application.DTOs;

/// <summary>
/// 検索結果のDTO。
/// </summary>
public class SearchResultDto
{
    /// <summary>検索クエリ</summary>
    public string Query { get; set; } = string.Empty;

    /// <summary>検索実行日時</summary>
    public DateTime SearchedAt { get; set; } = DateTime.UtcNow;

    /// <summary>検索結果一覧</summary>
    public List<SearchHitDto> Hits { get; set; } = [];

    /// <summary>結果件数</summary>
    public int TotalHits => Hits.Count;

    /// <summary>類似度閾値</summary>
    public float? Threshold { get; set; }

    /// <summary>要求件数</summary>
    public int? RequestedTop { get; set; }
}

/// <summary>
/// 検索ヒット情報のDTO。
/// </summary>
public class SearchHitDto
{
    /// <summary>ポイントID</summary>
    public Guid Id { get; set; }

    /// <summary>類似度スコア（0.0-1.0）</summary>
    public float Score { get; set; }

    /// <summary>パーツID</summary>
    public Guid? PartId { get; set; }

    /// <summary>型式</summary>
    public string? PartNumber { get; set; }

    /// <summary>パーツ名</summary>
    public string? PartName { get; set; }

    /// <summary>装置ID</summary>
    public Guid? AssetId { get; set; }

    /// <summary>装置名</summary>
    public string? AssetName { get; set; }

    /// <summary>プロジェクトID</summary>
    public Guid? ProjectId { get; set; }

    /// <summary>プロジェクト名</summary>
    public string? ProjectName { get; set; }

    /// <summary>コンテンツタイプ</summary>
    public string? Type { get; set; }

    /// <summary>コンテンツ（元テキスト）</summary>
    public string? Content { get; set; }

    /// <summary>ファイルパス</summary>
    public string? FilePath { get; set; }

    /// <summary>作成日時</summary>
    public DateTime? CreatedAt { get; set; }

    /// <summary>追加メタデータ</summary>
    public Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// 類似パーツ検索結果のDTO。
/// </summary>
public class SimilarPartsResultDto
{
    /// <summary>基準パーツID</summary>
    public Guid BasePartId { get; set; }

    /// <summary>基準パーツ型式</summary>
    public string BasePartNumber { get; set; } = string.Empty;

    /// <summary>類似パーツ一覧</summary>
    public List<SimilarPartDto> SimilarParts { get; set; } = [];
}

/// <summary>
/// 類似パーツ情報のDTO。
/// </summary>
public class SimilarPartDto
{
    /// <summary>パーツID</summary>
    public Guid PartId { get; set; }

    /// <summary>型式</summary>
    public string PartNumber { get; set; } = string.Empty;

    /// <summary>名称</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>種別</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>類似度スコア</summary>
    public float Score { get; set; }

    /// <summary>使用プロジェクト</summary>
    public string? ProjectName { get; set; }

    /// <summary>使用装置</summary>
    public string? AssetName { get; set; }

    /// <summary>類似ポイント（なぜ類似と判定されたか）</summary>
    public string? SimilarityReason { get; set; }
}

/// <summary>
/// 検索候補のDTO（オートコンプリート用）。
/// </summary>
public class SearchSuggestionDto
{
    /// <summary>候補テキスト</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>候補タイプ</summary>
    public SuggestionType Type { get; set; }

    /// <summary>関連アイテム数</summary>
    public int Count { get; set; }
}

/// <summary>
/// 検索候補のタイプ。
/// </summary>
public enum SuggestionType
{
    /// <summary>型式</summary>
    PartNumber,

    /// <summary>名称</summary>
    Name,

    /// <summary>材質</summary>
    Material,

    /// <summary>メーカー</summary>
    Manufacturer,

    /// <summary>プロジェクト</summary>
    Project,

    /// <summary>装置</summary>
    Asset,

    /// <summary>その他</summary>
    Other
}
