namespace DesignAid.Domain.ValueObjects;

/// <summary>
/// バリデーション結果の深刻度を表す列挙型。
/// </summary>
public enum ValidationSeverity
{
    /// <summary>正常</summary>
    Ok = 0,

    /// <summary>警告（処理は継続可能）</summary>
    Warning = 1,

    /// <summary>エラー（処理を中断すべき）</summary>
    Error = 2
}

/// <summary>
/// バリデーション詳細を表すレコード。
/// </summary>
/// <param name="Field">対象フィールド名</param>
/// <param name="Message">詳細メッセージ</param>
/// <param name="Severity">深刻度</param>
public record ValidationDetail(string Field, string Message, ValidationSeverity Severity);

/// <summary>
/// バリデーション結果を表す値オブジェクト。
/// 整合性検証や設計基準チェックの結果を格納する。
/// </summary>
public record ValidationResult
{
    /// <summary>深刻度</summary>
    public ValidationSeverity Severity { get; init; }

    /// <summary>メッセージ</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>対象（パーツ番号、ファイル名など）</summary>
    public string? Target { get; init; }

    /// <summary>詳細情報のリスト</summary>
    public IReadOnlyList<ValidationDetail> Details { get; init; } = [];

    /// <summary>成功かどうか</summary>
    public bool IsSuccess => Severity == ValidationSeverity.Ok;

    /// <summary>警告があるかどうか</summary>
    public bool HasWarnings => Severity == ValidationSeverity.Warning;

    /// <summary>エラーがあるかどうか</summary>
    public bool HasErrors => Severity == ValidationSeverity.Error;

    /// <summary>
    /// 成功結果を生成する。
    /// </summary>
    /// <param name="target">対象</param>
    /// <returns>成功を示す ValidationResult</returns>
    public static ValidationResult Ok(string? target = null) =>
        new() { Severity = ValidationSeverity.Ok, Target = target };

    /// <summary>
    /// 警告結果を生成する。
    /// </summary>
    /// <param name="message">警告メッセージ</param>
    /// <param name="target">対象</param>
    /// <returns>警告を示す ValidationResult</returns>
    public static ValidationResult Warning(string message, string? target = null) =>
        new() { Severity = ValidationSeverity.Warning, Message = message, Target = target };

    /// <summary>
    /// エラー結果を生成する。
    /// </summary>
    /// <param name="message">エラーメッセージ</param>
    /// <param name="target">対象</param>
    /// <returns>エラーを示す ValidationResult</returns>
    public static ValidationResult Error(string message, string? target = null) =>
        new() { Severity = ValidationSeverity.Error, Message = message, Target = target };

    /// <summary>
    /// 詳細付きの結果を生成する。
    /// </summary>
    /// <param name="severity">深刻度</param>
    /// <param name="message">メッセージ</param>
    /// <param name="details">詳細リスト</param>
    /// <param name="target">対象</param>
    /// <returns>ValidationResult</returns>
    public static ValidationResult WithDetails(
        ValidationSeverity severity,
        string message,
        IEnumerable<ValidationDetail> details,
        string? target = null) =>
        new()
        {
            Severity = severity,
            Message = message,
            Target = target,
            Details = details.ToList()
        };

    /// <summary>
    /// 複数の結果を集約する。
    /// 最も深刻な結果を全体の深刻度とする。
    /// </summary>
    /// <param name="results">集約する結果</param>
    /// <returns>集約された ValidationResult</returns>
    public static ValidationResult Aggregate(IEnumerable<ValidationResult> results)
    {
        var resultList = results.ToList();

        if (resultList.Count == 0)
            return Ok();

        var maxSeverity = resultList.Max(r => r.Severity);
        var allDetails = resultList
            .SelectMany(r => r.Details)
            .ToList();

        // 最も深刻な結果からメッセージを取得
        var primaryResult = resultList
            .Where(r => r.Severity == maxSeverity)
            .FirstOrDefault();

        return new ValidationResult
        {
            Severity = maxSeverity,
            Message = primaryResult?.Message ?? string.Empty,
            Target = primaryResult?.Target,
            Details = allDetails
        };
    }

    /// <summary>
    /// 詳細を追加した新しい結果を生成する。
    /// </summary>
    /// <param name="field">フィールド名</param>
    /// <param name="message">詳細メッセージ</param>
    /// <param name="severity">深刻度（省略時は結果の深刻度）</param>
    /// <returns>詳細が追加された ValidationResult</returns>
    public ValidationResult AddDetail(string field, string message, ValidationSeverity? severity = null)
    {
        var newDetail = new ValidationDetail(field, message, severity ?? Severity);
        var newDetails = Details.Append(newDetail).ToList();

        return this with { Details = newDetails };
    }
}

/// <summary>
/// ValidationSeverity の拡張メソッド。
/// </summary>
public static class ValidationSeverityExtensions
{
    /// <summary>
    /// 深刻度の表示名を取得する。
    /// </summary>
    public static string ToDisplayName(this ValidationSeverity severity) => severity switch
    {
        ValidationSeverity.Ok => "OK",
        ValidationSeverity.Warning => "WARNING",
        ValidationSeverity.Error => "ERROR",
        _ => throw new ArgumentOutOfRangeException(nameof(severity))
    };

    /// <summary>
    /// CLI 出力用のプレフィックスを取得する。
    /// </summary>
    public static string ToPrefix(this ValidationSeverity severity) => severity switch
    {
        ValidationSeverity.Ok => "[OK]",
        ValidationSeverity.Warning => "[WARNING]",
        ValidationSeverity.Error => "[ERROR]",
        _ => "[UNKNOWN]"
    };
}
