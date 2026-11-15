namespace DesignAid.Application.Services;

/// <summary>
/// プロジェクト単位の排他ロック。装置の状態（フォルダ・asset.json・asset_links.json・DB の行・索引）を
/// 変える処理（sync・asset add/update/remove/rename/link/unlink・part link・archive asset/restore・restore・
/// MCP の書き込み系）が、同時に 2 つ走って中間状態を取り込み合わないようにする。
/// <para>
/// design_aid.db と同じディレクトリのロックファイルを <c>FileShare.None</c> + <c>DeleteOnClose</c> で開き、
/// そのハンドルを持っている間がロック中である。<b>プロセスが落ちても OS がハンドルを閉じるので、ロックは残らない</b>
/// （ファイル自体は置いたままだが、開いていなければロックされていない）。
/// <b>取れなければ待たずに止める</b>（待つ形は、以前のロック待ちのハングの原因になるので入れない）。
/// </para>
/// <para>
/// 同じ呼び出しの流れ（AsyncLocal）の中で既に持っているときは、二重に取りにいかず、何もしないハンドルを返す
/// （外側の処理がロックを持ったまま内側の処理を呼んでも、自分自身とぶつからない）。再入の判定のキーとロックファイルの場所には、
/// junction・symlink を解決した実体のパスを使う（リンク経由で指した同じ場所を、同じ場所と見なす）。
/// </para>
/// </summary>
public static class ProjectLock
{
    /// <summary>ロックファイル名（design_aid.db と同じディレクトリに置く）。</summary>
    public const string FileName = ".daid.lock";

    private static readonly AsyncLocal<string?> Held = new();

    /// <summary>
    /// ロックを取る。取れなければ（別のプロセス・別の流れが持っている）null を返す。待たない。
    /// 共有違反・ロック違反以外の IO エラー（場所が無い、容量不足など）は、元の例外のまま投げる。
    /// </summary>
    /// <param name="dataDir">プロジェクトのルート（design_aid.db があるディレクトリ）</param>
    public static Handle? TryAcquire(string dataDir)
    {
        var key = ResolveKey(dataDir);

        // 同じ流れで既に持っている（再入）。キーは各階層を実際の綴りに直してあるので、そのまま（Ordinal で）比べる
        if (Held.Value != null && string.Equals(Held.Value, key, StringComparison.Ordinal))
            return new Handle(null, key, reentrant: true);

        try
        {
            // ファイルは置いたままにする（DeleteOnClose にしない）。FileShare.None で開いている間だけ保持する。
            // 削除の最中に開く競合（Windows の UnauthorizedAccessException）と、Unix で解放と取得が重なって
            // 別々の inode のファイルを同時に持つ隙がなくなる。
            // UnauthorizedAccessException（権限・ディレクトリ）は busy にせず、原因のまま投げる
            var stream = new FileStream(Path.Combine(key, FileName), FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None, 1, FileOptions.None);
            var previous = Held.Value;
            Held.Value = key;
            return new Handle(stream, previous, reentrant: false);
        }
        catch (IOException ex) when (IsBusy(ex))
        {
            return null;
        }
    }

    /// <summary>
    /// ファイル名がロックファイルか。Windows と macOS は大文字小文字を区別しないボリュームが既定なので
    /// 区別せずに比べ、Windows は末尾の <c>.</c> と空白を無視するので、除いた名前でも照らす。
    /// </summary>
    public static bool IsLockFileName(string name)
    {
        var cmp = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(name, FileName, cmp) || string.Equals(name.TrimEnd(' ', '.'), FileName, cmp);
    }

    /// <summary>
    /// 再入の判定に使うキー（<b>ロックファイルを置く場所</b>でもある）: フルパスにし、区切り文字をそろえ、末尾の区切りを除き、
    /// <b>junction・symlink を解決した実体のパス</b>にする。リンク経由で指した同じ場所が、別の場所と見なされて
    /// 自分自身のロックに誤って止められるのを防ぐ。リンクを解決できなければ（権限・ループなど）解決前のパスのまま
    /// （誤って止まる側に倒れる）。
    /// </summary>
    public static string ResolveKey(string dataDir)
        => ResolveLinks(TrimTrailingSeparators(Path.GetFullPath(dataDir).Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)), 0);

    /// <summary>
    /// 末尾の区切りを除く。<b>ルート（<c>/</c>・<c>C:\</c>）は保つ</b>（<c>TrimEnd</c> だけだと <c>/</c> が空文字になり、
    /// 以降の階層が相対パスとして連結されて、カレントディレクトリ基準の別の場所を指してしまう）。
    /// </summary>
    public static string TrimTrailingSeparators(string path)
    {
        var root = Path.GetPathRoot(path);
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar);
        return !string.IsNullOrEmpty(root) && trimmed.Length < root.Length ? root : trimmed;
    }

    /// <summary>
    /// 親ディレクトリの項目の名前から、<paramref name="name"/> のディスク上の実際の綴りを選ぶ（純関数）。
    /// 完全に一致する名前があればそれを優先し、無ければ大文字小文字を無視して一致する名前がちょうど 1 つのときだけそれを採る。
    /// 0 個・2 個以上（大文字小文字を区別するボリュームの別々の実体）のときは <paramref name="name"/> のまま。
    /// </summary>
    public static string PickActualName(string name, IEnumerable<string> siblingNames)
    {
        string? exact = null;
        var ignoreCase = new List<string>();
        foreach (var n in siblingNames)
        {
            if (string.Equals(n, name, StringComparison.Ordinal)) exact = n;
            else if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) ignoreCase.Add(n);
        }
        return exact ?? (ignoreCase.Count == 1 ? ignoreCase[0] : name);
    }

    /// <summary>
    /// 階層の名前を、親ディレクトリの中の実際の綴りに直す。大文字小文字を区別しないボリューム（Windows・macOS の既定）で、
    /// 同じ実体を別の綴りで指しても同じキーになり、区別するボリュームの別々の実体は別のキーのままになる。
    /// 調べられなければ（権限など）そのまま。
    /// </summary>
    private static string ActualName(string parent, string name)
    {
        try
        {
            var options = new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive, IgnoreInaccessible = true, AttributesToSkip = 0 };
            var siblings = Directory.EnumerateFileSystemEntries(parent, name, options).Select(Path.GetFileName).OfType<string>().ToList();
            return PickActualName(name, siblings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return name;
        }
    }

    /// <summary>リンクを辿る回数の上限（循環リンクで止まらなくなるのを防ぐ）。</summary>
    private const int MaxLinkDepth = 16;

    /// <summary>
    /// パスの各階層を根から順に見て、リンク（junction・symlink）であれば実体に置き換える。
    /// 最後の階層だけでなく、途中の階層がリンクでも解決する（<c>Directory.ResolveLinkTarget</c> は渡した階層しか見ないため）。
    /// </summary>
    private static string ResolveLinks(string fullPath, int depth)
    {
        if (depth > MaxLinkDepth) return fullPath;
        try
        {
            var root = Path.GetPathRoot(fullPath);
            if (string.IsNullOrEmpty(root)) return fullPath;
            // ルートの綴りは Windows ではドライブ文字・UNC の大文字小文字を区別しないので大文字にそろえる
            var current = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (OperatingSystem.IsWindows()) current = current.ToUpperInvariant();
            var parts = fullPath[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                current = Path.Combine(current, ActualName(current, part));
                FileSystemInfo? target = null;
                try { target = new DirectoryInfo(current).ResolveLinkTarget(returnFinalTarget: true); }
                catch (IOException) { /* 無い階層・リンクでない・ループ: そのまま */ }
                catch (UnauthorizedAccessException) { }
                if (target != null)
                {
                    // 実体のパスの途中にも別のリンクがありうるので、再帰して解決する
                    current = ResolveLinks(TrimTrailingSeparators(target.FullName), depth + 1);
                }
            }
            return TrimTrailingSeparators(current);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return fullPath;
        }
    }

    /// <summary>
    /// 例外が「別の誰かがロックファイルを持っている」ことを表すか。
    /// Windows は HResult の下位 16 ビットが 32（共有違反）か 33（ロック違反）。
    /// Unix では .NET の FileShare の競合は flock の EWOULDBLOCK を errno のまま HResult に入れた
    /// IOException になる（Linux は 11、macOS は 35）。ディスク容量不足・I/O 障害・場所が無いなどは含めない。
    /// </summary>
    public static bool IsBusy(IOException ex)
    {
        if (ex is DirectoryNotFoundException or FileNotFoundException or PathTooLongException or DriveNotFoundException)
            return false;
        var code = ex.HResult & 0xFFFF;
        return code is 32 or 33 || ex.HResult is 11 or 35;
    }

    /// <summary>ロックのハンドル。Dispose で解放する。</summary>
    public sealed class Handle : IDisposable
    {
        private FileStream? _stream;
        private readonly string? _restore;
        private readonly bool _reentrant;

        internal Handle(FileStream? stream, string? restore, bool reentrant)
        {
            _stream = stream;
            _restore = restore;
            _reentrant = reentrant;
        }

        /// <summary>再入（外側が既に持っている）なら true。</summary>
        public bool IsReentrant => _reentrant;

        /// <inheritdoc />
        public void Dispose()
        {
            if (_reentrant || _stream == null) return;
            _stream.Dispose();
            _stream = null;
            Held.Value = _restore;
        }
    }
}
