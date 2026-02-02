using System.Text.RegularExpressions;

namespace DesignAid.Domain.ValueObjects;

/// <summary>
/// 型式を表す値オブジェクト。
/// 英数字、ハイフン、アンダースコア、ピリオドを許可。
/// </summary>
public readonly partial record struct PartNumber
{
    private static readonly Regex ValidPattern = CreateValidPattern();

    /// <summary>型式の値</summary>
    public string Value { get; }

    /// <summary>
    /// 型式を生成する。
    /// </summary>
    /// <param name="value">型式文字列</param>
    /// <exception cref="ArgumentException">値が空または不正な形式の場合</exception>
    public PartNumber(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("型式は必須です", nameof(value));

        if (!ValidPattern.IsMatch(value))
            throw new ArgumentException(
                "型式には英数字、ハイフン、アンダースコア、ピリオドのみ使用可能です",
                nameof(value));

        Value = value;
    }

    /// <summary>文字列への暗黙的変換</summary>
    public override string ToString() => Value;

    /// <summary>文字列への暗黙的変換演算子</summary>
    public static implicit operator string(PartNumber pn) => pn.Value;

    /// <summary>
    /// 型式の有効性を検証する（例外をスローしない）。
    /// </summary>
    /// <param name="value">検証する値</param>
    /// <returns>有効な場合は true</returns>
    public static bool IsValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return ValidPattern.IsMatch(value);
    }

    /// <summary>
    /// 型式の生成を試みる。
    /// </summary>
    /// <param name="value">型式文字列</param>
    /// <param name="result">成功時の PartNumber</param>
    /// <returns>成功した場合は true</returns>
    public static bool TryCreate(string? value, out PartNumber result)
    {
        if (!IsValid(value))
        {
            result = default;
            return false;
        }

        result = new PartNumber(value!);
        return true;
    }

    [GeneratedRegex(@"^[A-Za-z0-9\-_.]+$", RegexOptions.Compiled)]
    private static partial Regex CreateValidPattern();
}
