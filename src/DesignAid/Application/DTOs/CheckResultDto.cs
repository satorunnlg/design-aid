namespace DesignAid.Application.DTOs;

/// <summary>
/// 整合性チェック結果のDTO。
/// </summary>
public class CheckResultDto
{
    /// <summary>チェック実行日時</summary>
    public DateTime CheckedAt { get; set; } = DateTime.UtcNow;

    /// <summary>チェック対象パス</summary>
    public string? TargetPath { get; set; }

    /// <summary>チェック結果一覧</summary>
    public List<PartCheckResultDto> Results { get; set; } = [];

    /// <summary>成功</summary>
    public bool Success => ErrorCount == 0;

    /// <summary>OK 数</summary>
    public int OkCount => Results.Count(r => r.Status == CheckStatus.Ok);

    /// <summary>警告数</summary>
    public int WarningCount => Results.Count(r => r.Status == CheckStatus.Warning);

    /// <summary>エラー数</summary>
    public int ErrorCount => Results.Count(r => r.Status == CheckStatus.Error);

    /// <summary>総チェック数</summary>
    public int TotalCount => Results.Count;

    /// <summary>サマリーメッセージ</summary>
    public string Summary => $"{OkCount} OK, {WarningCount} Warning, {ErrorCount} Error";
}

/// <summary>
/// パーツ単位のチェック結果。
/// </summary>
public class PartCheckResultDto
{
    /// <summary>パーツID</summary>
    public Guid PartId { get; set; }

    /// <summary>型式</summary>
    public string PartNumber { get; set; } = string.Empty;

    /// <summary>名称</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>チェックステータス</summary>
    public CheckStatus Status { get; set; }

    /// <summary>メッセージ</summary>
    public string? Message { get; set; }

    /// <summary>ファイル別チェック結果</summary>
    public List<FileCheckResultDto> FileResults { get; set; } = [];

    /// <summary>エラー詳細</summary>
    public List<string> Errors { get; set; } = [];

    /// <summary>警告詳細</summary>
    public List<string> Warnings { get; set; } = [];
}

/// <summary>
/// ファイル単位のチェック結果。
/// </summary>
public class FileCheckResultDto
{
    /// <summary>ファイルパス（相対）</summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>チェックステータス</summary>
    public CheckStatus Status { get; set; }

    /// <summary>メッセージ</summary>
    public string? Message { get; set; }

    /// <summary>期待されるハッシュ</summary>
    public string? ExpectedHash { get; set; }

    /// <summary>実際のハッシュ</summary>
    public string? ActualHash { get; set; }

    /// <summary>ファイル存在フラグ</summary>
    public bool FileExists { get; set; }
}

/// <summary>
/// チェックステータス。
/// </summary>
public enum CheckStatus
{
    /// <summary>正常</summary>
    Ok,

    /// <summary>警告（不整合あり）</summary>
    Warning,

    /// <summary>エラー（ファイル不存在等）</summary>
    Error
}

/// <summary>
/// バリデーション結果のDTO。
/// </summary>
public class VerifyResultDto
{
    /// <summary>検証実行日時</summary>
    public DateTime VerifiedAt { get; set; } = DateTime.UtcNow;

    /// <summary>検証結果一覧</summary>
    public List<PartVerifyResultDto> Results { get; set; } = [];

    /// <summary>成功</summary>
    public bool Success => FailCount == 0;

    /// <summary>合格数</summary>
    public int PassCount => Results.Count(r => r.Passed);

    /// <summary>不合格数</summary>
    public int FailCount => Results.Count(r => !r.Passed);

    /// <summary>総検証数</summary>
    public int TotalCount => Results.Count;

    /// <summary>サマリーメッセージ</summary>
    public string Summary => $"{PassCount} Pass, {FailCount} Fail";
}

/// <summary>
/// パーツ単位のバリデーション結果。
/// </summary>
public class PartVerifyResultDto
{
    /// <summary>パーツID</summary>
    public Guid PartId { get; set; }

    /// <summary>型式</summary>
    public string PartNumber { get; set; } = string.Empty;

    /// <summary>名称</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>合格フラグ</summary>
    public bool Passed { get; set; }

    /// <summary>基準別検証結果</summary>
    public List<StandardVerifyResultDto> StandardResults { get; set; } = [];
}

/// <summary>
/// 設計基準単位の検証結果。
/// </summary>
public class StandardVerifyResultDto
{
    /// <summary>基準ID</summary>
    public string StandardId { get; set; } = string.Empty;

    /// <summary>基準名</summary>
    public string StandardName { get; set; } = string.Empty;

    /// <summary>合格フラグ</summary>
    public bool Passed { get; set; }

    /// <summary>メッセージ</summary>
    public string? Message { get; set; }

    /// <summary>推奨事項</summary>
    public string? Recommendation { get; set; }
}
