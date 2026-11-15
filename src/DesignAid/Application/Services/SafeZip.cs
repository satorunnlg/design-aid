namespace DesignAid.Application.Services;

/// <summary>
/// zip を展開するときの安全確認（zip-slip の対策）。エントリの名前が <c>..\</c> や絶対パスで、
/// 展開先の外を指していないかを、展開する前に確かめる。
/// </summary>
public static class SafeZip
{
    /// <summary>
    /// 展開先の外を指すエントリがあれば、その名前を返す。無ければ null。
    /// </summary>
    /// <param name="destDir">展開先のディレクトリ</param>
    /// <param name="entryNames">zip のエントリの名前（<c>FullName</c>）</param>
    public static string? FindEscapingEntry(string destDir, IEnumerable<string> entryNames)
    {
        var root = Path.GetFullPath(destDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                   + Path.DirectorySeparatorChar;
        var cmp = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        foreach (var name in entryNames)
        {
            var full = Path.GetFullPath(Path.Combine(destDir, name));
            if (!full.StartsWith(root, cmp) && !string.Equals(full + Path.DirectorySeparatorChar, root, cmp))
                return name;
        }
        return null;
    }
}
