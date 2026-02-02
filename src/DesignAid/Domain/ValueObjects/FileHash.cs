using System.Text.RegularExpressions;

namespace DesignAid.Domain.ValueObjects;

/// <summary>
/// ファイルハッシュを表す値オブジェクト。
/// SHA256 形式（sha256:xxxxxxxx...）を想定。
/// </summary>
public readonly partial record struct FileHash
{
    /// <summary>SHA256 ハッシュのプレフィックス</summary>
    public const string Sha256Prefix = "sha256:";

    /// <summary>SHA256 ハッシュの16進数文字列長</summary>
    public const int Sha256HexLength = 64;

    private static readonly Regex Sha256Pattern = CreateSha256Pattern();

    /// <summary>ハッシュ値（プレフィックス付き）</summary>
    public string Value { get; }

    /// <summary>
    /// ファイルハッシュを生成する。
    /// </summary>
    /// <param name="value">ハッシュ値（sha256:xxxxxxxx... 形式）</param>
    /// <exception cref="ArgumentException">値が空または不正な形式の場合</exception>
    public FileHash(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("ハッシュ値は必須です", nameof(value));

        if (!Sha256Pattern.IsMatch(value))
            throw new ArgumentException(
                "ハッシュ値は sha256: プレフィックスと64文字の16進数で構成される必要があります",
                nameof(value));

        Value = value.ToLowerInvariant();
    }

    /// <summary>文字列への暗黙的変換</summary>
    public override string ToString() => Value;

    /// <summary>文字列への暗黙的変換演算子</summary>
    public static implicit operator string(FileHash hash) => hash.Value;

    /// <summary>
    /// 16進数文字列から FileHash を生成する。
    /// </summary>
    /// <param name="hexString">64文字の16進数文字列（プレフィックスなし）</param>
    /// <returns>FileHash インスタンス</returns>
    public static FileHash FromHex(string hexString)
    {
        if (string.IsNullOrWhiteSpace(hexString))
            throw new ArgumentException("16進数文字列は必須です", nameof(hexString));

        // プレフィックス付きの場合はそのまま処理
        if (hexString.StartsWith(Sha256Prefix, StringComparison.OrdinalIgnoreCase))
            return new FileHash(hexString);

        // プレフィックスなしの場合は追加
        return new FileHash(Sha256Prefix + hexString.ToLowerInvariant());
    }

    /// <summary>
    /// バイト配列から FileHash を生成する。
    /// </summary>
    /// <param name="hashBytes">ハッシュのバイト配列（32バイト）</param>
    /// <returns>FileHash インスタンス</returns>
    public static FileHash FromBytes(byte[] hashBytes)
    {
        if (hashBytes == null || hashBytes.Length != 32)
            throw new ArgumentException("SHA256 ハッシュは32バイトである必要があります", nameof(hashBytes));

        var hexString = Convert.ToHexString(hashBytes).ToLowerInvariant();
        return new FileHash(Sha256Prefix + hexString);
    }

    /// <summary>
    /// ハッシュ値の有効性を検証する（例外をスローしない）。
    /// </summary>
    /// <param name="value">検証する値</param>
    /// <returns>有効な場合は true</returns>
    public static bool IsValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return Sha256Pattern.IsMatch(value);
    }

    /// <summary>
    /// ハッシュの生成を試みる。
    /// </summary>
    /// <param name="value">ハッシュ文字列</param>
    /// <param name="result">成功時の FileHash</param>
    /// <returns>成功した場合は true</returns>
    public static bool TryCreate(string? value, out FileHash result)
    {
        if (!IsValid(value))
        {
            result = default;
            return false;
        }

        result = new FileHash(value!);
        return true;
    }

    /// <summary>
    /// プレフィックスを除いた16進数文字列を取得する。
    /// </summary>
    public string ToHexString() => Value[Sha256Prefix.Length..];

    [GeneratedRegex(@"^sha256:[a-fA-F0-9]{64}$", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex CreateSha256Pattern();
}
