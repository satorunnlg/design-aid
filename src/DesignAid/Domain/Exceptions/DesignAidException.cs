namespace DesignAid.Domain.Exceptions;

/// <summary>
/// Design Aid の基底例外クラス。
/// 全てのカスタム例外はこのクラスを継承する。
/// </summary>
public abstract class DesignAidException : Exception
{
    /// <summary>
    /// CLI 終了コード。
    /// 0=成功, 1=一般エラー, 2=引数エラー, 3=設定エラー, 4=接続エラー, 5=整合性エラー
    /// </summary>
    public int ExitCode { get; }

    /// <summary>
    /// エラーコード（ログや JSON 出力用）。
    /// </summary>
    public string ErrorCode { get; }

    /// <summary>
    /// 基底例外を生成する。
    /// </summary>
    /// <param name="message">エラーメッセージ</param>
    /// <param name="exitCode">CLI 終了コード</param>
    /// <param name="errorCode">エラーコード</param>
    /// <param name="inner">内部例外</param>
    protected DesignAidException(
        string message,
        int exitCode,
        string errorCode,
        Exception? inner = null)
        : base(message, inner)
    {
        ExitCode = exitCode;
        ErrorCode = errorCode;
    }
}

/// <summary>
/// 一般的なランタイムエラー（終了コード: 1）。
/// </summary>
public class GeneralException : DesignAidException
{
    /// <summary>
    /// 一般エラーを生成する。
    /// </summary>
    /// <param name="message">エラーメッセージ</param>
    /// <param name="inner">内部例外</param>
    public GeneralException(string message, Exception? inner = null)
        : base(message, exitCode: 1, errorCode: "GENERAL_ERROR", inner)
    {
    }
}

/// <summary>
/// コマンドライン引数エラー（終了コード: 2）。
/// </summary>
public class ArgumentValidationException : DesignAidException
{
    /// <summary>不正な引数名</summary>
    public string ArgumentName { get; }

    /// <summary>
    /// 引数エラーを生成する。
    /// </summary>
    /// <param name="argumentName">引数名</param>
    /// <param name="message">エラーメッセージ</param>
    /// <param name="inner">内部例外</param>
    public ArgumentValidationException(string argumentName, string message, Exception? inner = null)
        : base($"引数 '{argumentName}' が不正です: {message}", exitCode: 2, errorCode: "INVALID_ARGUMENT", inner)
    {
        ArgumentName = argumentName;
    }
}

/// <summary>
/// 設定関連のエラー（終了コード: 3）。
/// </summary>
public class ConfigurationException : DesignAidException
{
    /// <summary>設定キー（該当する場合）</summary>
    public string? ConfigKey { get; }

    /// <summary>
    /// 設定エラーを生成する。
    /// </summary>
    /// <param name="message">エラーメッセージ</param>
    /// <param name="configKey">設定キー</param>
    /// <param name="inner">内部例外</param>
    public ConfigurationException(string message, string? configKey = null, Exception? inner = null)
        : base(message, exitCode: 3, errorCode: "CONFIGURATION_ERROR", inner)
    {
        ConfigKey = configKey;
    }

    /// <summary>
    /// 必須設定が見つからない場合のエラーを生成する。
    /// </summary>
    /// <param name="configKey">設定キー</param>
    /// <returns>ConfigurationException</returns>
    public static ConfigurationException MissingRequired(string configKey)
        => new($"必須の設定 '{configKey}' が見つかりません", configKey);

    /// <summary>
    /// 設定値が不正な場合のエラーを生成する。
    /// </summary>
    /// <param name="configKey">設定キー</param>
    /// <param name="value">不正な値</param>
    /// <returns>ConfigurationException</returns>
    public static ConfigurationException InvalidValue(string configKey, string? value)
        => new($"設定 '{configKey}' の値が不正です: {value ?? "(null)"}", configKey);
}

/// <summary>
/// 外部サービス接続エラー（終了コード: 4）。
/// </summary>
public class ConnectionException : DesignAidException
{
    /// <summary>接続先サービス名</summary>
    public string ServiceName { get; }

    /// <summary>接続先ホスト（該当する場合）</summary>
    public string? Host { get; }

    /// <summary>接続先ポート（該当する場合）</summary>
    public int? Port { get; }

    /// <summary>
    /// 接続エラーを生成する。
    /// </summary>
    /// <param name="serviceName">サービス名</param>
    /// <param name="message">エラーメッセージ</param>
    /// <param name="host">ホスト</param>
    /// <param name="port">ポート</param>
    /// <param name="inner">内部例外</param>
    public ConnectionException(
        string serviceName,
        string message,
        string? host = null,
        int? port = null,
        Exception? inner = null)
        : base($"{serviceName}: {message}", exitCode: 4, errorCode: "CONNECTION_ERROR", inner)
    {
        ServiceName = serviceName;
        Host = host;
        Port = port;
    }

    /// <summary>
    /// 埋め込みサービス接続エラーを生成する。
    /// </summary>
    /// <param name="providerName">プロバイダー名</param>
    /// <param name="inner">内部例外</param>
    /// <returns>ConnectionException</returns>
    public static ConnectionException EmbeddingServiceFailed(string providerName, Exception? inner = null)
        => new($"Embedding ({providerName})", "埋め込みサービスへの接続に失敗しました", inner: inner);
}

/// <summary>
/// 整合性検証エラー（終了コード: 5）。
/// </summary>
public class IntegrityException : DesignAidException
{
    /// <summary>対象パーツ番号（該当する場合）</summary>
    public string? PartNumber { get; }

    /// <summary>対象ファイルパス（該当する場合）</summary>
    public string? FilePath { get; }

    /// <summary>
    /// 整合性エラーを生成する。
    /// </summary>
    /// <param name="message">エラーメッセージ</param>
    /// <param name="partNumber">パーツ番号</param>
    /// <param name="filePath">ファイルパス</param>
    /// <param name="inner">内部例外</param>
    public IntegrityException(
        string message,
        string? partNumber = null,
        string? filePath = null,
        Exception? inner = null)
        : base(message, exitCode: 5, errorCode: "INTEGRITY_ERROR", inner)
    {
        PartNumber = partNumber;
        FilePath = filePath;
    }

    /// <summary>
    /// ハッシュ不整合エラーを生成する。
    /// </summary>
    /// <param name="filePath">ファイルパス</param>
    /// <param name="expected">期待されるハッシュ</param>
    /// <param name="actual">実際のハッシュ</param>
    /// <param name="partNumber">パーツ番号</param>
    /// <returns>IntegrityException</returns>
    public static IntegrityException HashMismatch(
        string filePath,
        string expected,
        string actual,
        string? partNumber = null)
        => new(
            $"ファイル '{filePath}' のハッシュが一致しません。期待値: {expected}, 実際: {actual}",
            partNumber,
            filePath);

    /// <summary>
    /// ファイル未検出エラーを生成する。
    /// </summary>
    /// <param name="filePath">ファイルパス</param>
    /// <param name="partNumber">パーツ番号</param>
    /// <returns>IntegrityException</returns>
    public static IntegrityException FileNotFound(string filePath, string? partNumber = null)
        => new($"ファイル '{filePath}' が見つかりません", partNumber, filePath);

    /// <summary>
    /// データ破損検知エラーを生成する。
    /// </summary>
    /// <param name="description">破損の説明</param>
    /// <param name="partNumber">パーツ番号</param>
    /// <returns>IntegrityException</returns>
    public static IntegrityException DataCorruption(string description, string? partNumber = null)
        => new($"データ破損を検知しました: {description}", partNumber);
}

/// <summary>
/// エンティティが見つからないエラー（終了コード: 1）。
/// </summary>
public class EntityNotFoundException : DesignAidException
{
    /// <summary>エンティティ種別</summary>
    public string EntityType { get; }

    /// <summary>検索に使用した識別子</summary>
    public string Identifier { get; }

    /// <summary>
    /// エンティティ未検出エラーを生成する。
    /// </summary>
    /// <param name="entityType">エンティティ種別</param>
    /// <param name="identifier">識別子</param>
    /// <param name="inner">内部例外</param>
    public EntityNotFoundException(string entityType, string identifier, Exception? inner = null)
        : base($"{entityType} '{identifier}' が見つかりません", exitCode: 1, errorCode: "ENTITY_NOT_FOUND", inner)
    {
        EntityType = entityType;
        Identifier = identifier;
    }

    /// <summary>
    /// プロジェクト未検出エラーを生成する。
    /// </summary>
    public static EntityNotFoundException ProjectNotFound(string identifier)
        => new("プロジェクト", identifier);

    /// <summary>
    /// 装置未検出エラーを生成する。
    /// </summary>
    public static EntityNotFoundException AssetNotFound(string identifier)
        => new("装置", identifier);

    /// <summary>
    /// パーツ未検出エラーを生成する。
    /// </summary>
    public static EntityNotFoundException PartNotFound(string identifier)
        => new("パーツ", identifier);
}

/// <summary>
/// バリデーション失敗エラー（終了コード: 1）。
/// </summary>
public class ValidationException : DesignAidException
{
    /// <summary>バリデーション対象</summary>
    public string? Target { get; }

    /// <summary>バリデーションエラーの詳細リスト</summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>
    /// バリデーションエラーを生成する。
    /// </summary>
    /// <param name="message">エラーメッセージ</param>
    /// <param name="target">対象</param>
    /// <param name="errors">エラー詳細リスト</param>
    /// <param name="inner">内部例外</param>
    public ValidationException(
        string message,
        string? target = null,
        IEnumerable<string>? errors = null,
        Exception? inner = null)
        : base(message, exitCode: 1, errorCode: "VALIDATION_ERROR", inner)
    {
        Target = target;
        Errors = errors?.ToList() ?? [];
    }
}
