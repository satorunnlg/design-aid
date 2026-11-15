using System.Text.Json;
using System.Text.Json.Nodes;

namespace DesignAid.Application.Services;

/// <summary>
/// 装置の改名（<c>asset rename</c>）の判定を担う純関数の集まり。
/// ファイルにも DB にも触れない。実行は <see cref="AssetRenameService"/> が行い、
/// 「何をしてよいか・何を書くか」の判断だけをここに置く（単体テストで固めるため）。
/// </summary>
public static class AssetRenamePlanner
{
    /// <summary>Windows の予約デバイス名（拡張子付きでも予約として扱われる）。</summary>
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        // 上付き数字（U+00B9 U+00B2 U+00B3）も Windows では予約デバイス名として扱われる
        "COM\u00B9", "COM\u00B2", "COM\u00B3", "LPT\u00B9", "LPT\u00B2", "LPT\u00B3"
    };

    /// <summary>
    /// 装置名として使える名前かを検査する。装置名はそのままフォルダ名になる。
    /// </summary>
    /// <returns>使えるなら null。使えないなら理由。</returns>
    public static string? ValidateName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "装置名が空です";
        if (name != name.Trim())
            return "装置名の前後に空白は使えません";
        if (name is "." or "..")
            return $"'{name}' は装置名に使えません";
        if (name.EndsWith('.'))
            return "装置名の末尾に '.' は使えません（Windows でフォルダ名にできません）";
        if (name.Length > 255)
            return "装置名が長すぎます（255 文字まで）";

        // 孤立サロゲートは UTF-8 に変換できず、Normalize が例外になる。理由付きで拒否する
        for (var k = 0; k < name.Length; k++)
        {
            if (char.IsHighSurrogate(name[k]) && k + 1 < name.Length && char.IsLowSurrogate(name[k + 1])) { k++; continue; }
            if (char.IsSurrogate(name[k]))
                return "装置名に不正な Unicode（対になっていないサロゲート）が含まれています";
        }

        // Path.GetInvalidFileNameChars は OS で内容が違う（Unix では '/' と NUL しか返さない）ので使わず、
        // どこにクローンしても同じ結果になるよう Windows の禁止文字を固定で持つ（Unix の禁止文字も含まれる）。
        const string forbidden = "<>:\"/\\|?*";
        foreach (var c in name)
        {
            if (c < ' ' || forbidden.Contains(c))
                return $"装置名に使えない文字が含まれています: '{(c < ' ' ? $"U+{(int)c:X4}" : c.ToString())}'";
        }

        // CON.txt のように拡張子が付いても予約名になる
        // 拡張子の前の部分。末尾の空白とドットは Windows が無視するので除いて照らす（"CON .txt" も予約名）
        var stem = name.Split('.')[0].TrimEnd(' ', '.');
        if (ReservedNames.Contains(stem))
            return $"'{name}' は OS の予約名のため使えません";

        return null;
    }

    /// <summary>ファイル名の上限（UTF-8 のバイト数）。Linux の一般的なファイルシステムは 255 文字ではなく 255 バイト。</summary>
    public const int MaxNameBytes = 255;

    /// <summary>
    /// <b>新しく付ける名前</b>の検査。<see cref="ValidateName"/> に加えて UTF-8 で 255 バイトの上限を課す。
    /// 旧名にはこの上限を課さない（既存の長い名前を改名できなくならないように）。
    /// </summary>
    public static string? ValidateNewName(string? name)
    {
        var error = ValidateName(name);
        if (error != null) return error;
        // macOS（HFS+/APFS 互換）はファイル名を NFD に分解して保存するので、NFC と NFD の大きい方で数える
        var bytes = Math.Max(
            System.Text.Encoding.UTF8.GetByteCount(name!.Normalize(System.Text.NormalizationForm.FormC)),
            System.Text.Encoding.UTF8.GetByteCount(name.Normalize(System.Text.NormalizationForm.FormD)));
        if (bytes > MaxNameBytes)
            return $"装置名が長すぎます（UTF-8 で {MaxNameBytes} バイトまで。日本語は 1 文字 3 バイトです）";
        return null;
    }

    /// <summary>改名の可否を決めるための、事前に集めた事実。</summary>
    public sealed record Facts(
        string OldName,
        string NewName,
        bool OldInDb,
        bool OldHasAssetJson,
        bool OldAssetJsonUnreadable,
        bool NewInDb,
        bool NewHasAssetJson,
        bool NewPathExists,
        bool OldDirOutsideAssets = false,
        bool OldArchived = false,
        IReadOnlyList<string>? OldCaseVariants = null,
        IReadOnlyList<string>? ArchivesReferencingOld = null,
        IReadOnlyList<string>? UnreadableArchives = null,
        string? OldAssetJsonName = null,
        IReadOnlyList<string>? NewFsCaseCollisions = null,
        bool NewArchived = false,
        string? OldJsonIdOwner = null,
        IReadOnlyList<string>? NewJsonNameOwners = null,
        bool RenameInProgress = false);

    /// <summary>
    /// 履歴の土台を決める。asset.json に <c>previous_names</c> の欄が無い（null）ときは DB の履歴を引き継ぎ、
    /// 明示的な空の配列のときは asset.json を正とする（空として扱う）。asset.json が無ければ DB。
    /// </summary>
    public static IReadOnlyList<string>? ChoosePreviousNamesBase(
        bool hasJson, IReadOnlyList<string>? jsonNames, IReadOnlyList<string>? dbNames)
        => hasJson ? (jsonNames ?? dbNames) : dbNames;

    /// <summary>
    /// <paramref name="path"/> を正規化した親が <paramref name="assetsDir"/> と一致するか（assets 直下か）。
    /// 絶対パスや <c>..</c> で assets の外を指していないかの確認に使う。
    /// </summary>
    public static bool IsDirectChildOf(string assetsDir, string path)
    {
        var parent = Path.GetDirectoryName(Path.GetFullPath(path));
        if (parent == null) return false;
        var root = Path.GetFullPath(assetsDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        parent = parent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var cmp = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(parent, root, cmp);
    }

    /// <summary>
    /// 改名を始めてよいかを判定する。<b>1 つでも当てはまれば何も変えずに止める</b>ための一覧を返す。
    /// </summary>
    /// <returns>止める理由。空なら進めてよい。</returns>
    public static IReadOnlyList<string> CheckPreconditions(Facts f)
    {
        var errors = new List<string>();

        if (f.RenameInProgress)
        {
            errors.Add("別の変更が実行中です（改名・sync など、同じプロジェクトを変更する処理は同時に 1 つしか実行できません）。終わってからやり直してください");
            return errors;
        }

        // 旧名も単一のディレクトリ名に限る（絶対パスや .. で assets の外を指させない）
        var oldNameError = ValidateName(f.OldName);
        if (oldNameError != null)
        {
            errors.Add($"旧名が使えません: {oldNameError}");
            return errors;
        }
        if (f.OldDirOutsideAssets)
        {
            errors.Add($"旧名が assets 直下の装置ディレクトリを指していません: {f.OldName}");
            return errors;
        }

        var nameError = ValidateNewName(f.NewName);
        if (nameError != null)
        {
            errors.Add($"新しい名前が使えません: {nameError}");
            return errors;
        }

        if (f.OldName == f.NewName)
        {
            errors.Add("旧名と新名が同じです");
            return errors;
        }

        // Windows ではフォルダ名の大文字小文字が区別されないため、大文字小文字だけの違いは扱わない
        if (string.Equals(f.OldName, f.NewName, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("旧名と新名が大文字小文字の違いだけです（フォルダ名が区別されない環境があるため、改名できません）");
            return errors;
        }

        if (!f.OldInDb && !f.OldHasAssetJson)
            errors.Add($"改名元の装置が見つかりません: {f.OldName}（DB にも asset.json にもありません）");

        // 大小文字だけ違う名前（asset.json の name・DB の Name・別の装置）があれば、補正せずに拒否する。
        // 補正すると DB の更新を迂回したり、別の装置を巻き込んだりする。
        if (f.OldCaseVariants is { Count: > 0 })
            errors.Add($"旧名 '{f.OldName}' と大文字小文字だけが違う名前の装置があります: {string.Join(", ", f.OldCaseVariants)}（正しい名前をそのまま指定してください。別の装置の場合は改名できません）");

        // asset.json があるなら、その name が旧名と完全に一致することを必須にする（別装置の asset.json のコピーなどを巻き込まない）
        if (f.OldHasAssetJson && !f.OldAssetJsonUnreadable && f.OldAssetJsonName != null
            && !string.Equals(f.OldAssetJsonName, f.OldName, StringComparison.Ordinal))
            errors.Add($"asset.json の name '{f.OldAssetJsonName}' が旧名 '{f.OldName}' と一致しません（補正せずに止めます。asset.json か指定した名前を確認してください）");

        if (f.NewFsCaseCollisions is { Count: > 0 })
            errors.Add($"新しい名前と大文字小文字だけが違うものが assets/ にあります: {string.Join(", ", f.NewFsCaseCollisions)}");

        if (f.NewJsonNameOwners is { Count: > 0 })
            errors.Add($"新しい名前を asset.json の name に持つ別のフォルダがあります: {string.Join(", ", f.NewJsonNameOwners)}（未同期の装置と名前が衝突します）");

        if (f.NewArchived)
            errors.Add($"同じ名前のアーカイブがあります: {f.NewName}（アーカイブ済みの装置と名前が衝突するため改名できません）");

        if (f.OldJsonIdOwner != null)
            errors.Add($"更新対象の装置 ID が別の装置のものです: {f.OldJsonIdOwner}（asset.json をコピーした可能性があります。確認してください）");

        if (f.UnreadableArchives is { Count: > 0 })
            errors.Add(FormatUnreadableArchivesError(f.UnreadableArchives));

        if (f.OldArchived)
            errors.Add($"改名元の装置はアーカイブ済みです（先に `daid archive restore asset <名前>` の <名前> を次の名前に置き換えて（空白や記号を含むときはシェルに合わせて引用符で囲んで）復元してください）。\n  名前: {f.OldName}");

        if (f.ArchivesReferencingOld is { Count: > 0 })
            errors.Add($"アーカイブ内の asset_links.json が旧名 '{f.OldName}' を子装置として参照しています: {string.Join(", ", f.ArchivesReferencingOld)}（その親装置を先に daid archive restore asset で復元してください）");

        if (f.OldHasAssetJson && f.OldAssetJsonUnreadable)
            errors.Add($"改名元の asset.json を読み込めません: {f.OldName}");

        if (f.NewInDb)
            errors.Add($"新しい名前の装置が DB に既にあります: {f.NewName}");

        if (f.NewHasAssetJson)
            errors.Add($"新しい名前の装置が asset.json として既にあります: {f.NewName}");

        if (f.NewPathExists)
            errors.Add($"移動先が既に存在します: assets/{f.NewName}（ファイルを含む）");

        return errors;
    }

    /// <summary>
    /// 「変更前の名前」の一覧を更新する。古い順に追記し、重複させない。
    /// 新しい名前と同じものは一覧から外す（A→B→A と戻したとき、現在名が履歴に残らないように）。
    /// </summary>
    public static List<string> UpdatePreviousNames(
        IReadOnlyList<string>? existing, string oldName, string newName)
    {
        var result = new List<string>();
        foreach (var n in existing ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(n)) continue;
            if (string.Equals(n, newName, StringComparison.OrdinalIgnoreCase)) continue;
            if (result.Contains(n, StringComparer.OrdinalIgnoreCase)) continue;
            result.Add(n);
        }

        if (!result.Contains(oldName, StringComparer.OrdinalIgnoreCase))
            result.Add(oldName);

        return result;
    }

    /// <summary>
    /// 改名後の表示名を決める。表示名が旧名と同じ（＝名前をそのまま表示名にしていた）なら新名に変え、
    /// 違えば（利用者が付けた表示名なので）変えない。
    /// </summary>
    /// <returns>変えるなら新しい表示名。変えないなら null。</returns>
    public static string? ResolveDisplayName(string? currentDisplayName, string oldName, string newName)
        => currentDisplayName == oldName ? newName : null;

    /// <summary>
    /// 読めない zip がアーカイブにあって改名を止めるときの、エラーの文を組み立てる（純関数）。
    /// 止める理由（その zip が旧名を子装置として参照しているかもしれない）に加えて、
    /// <b>どの zip か（パス）</b>と<b>直し方</b>（その zip を退避する、または <c>archive restore</c> で戻す）を出す。
    /// </summary>
    public static string FormatUnreadableArchivesError(IReadOnlyList<string> zipPaths)
    {
        var paths = string.Join("、", zipPaths.Select(Path.GetFullPath));
        // 名前はコマンドの文字列に埋め込まず、別の行で示す（空白・引用符・先頭の - を含む名前が、実行すると壊れる案内にならないように）
        var names = string.Join("", zipPaths.Select(z => $"\n  名前: {Path.GetFileNameWithoutExtension(z)}"));
        return $"検査できないアーカイブがあります（その zip が旧名を子装置として参照しているかもしれず、確認できないまま改名はできません）: {paths}。" +
               $"直し方: その zip を archive/assets/ の外へ退避する（壊れていて不要ならそのまま退避してよい）か、" +
               $"アーカイブした装置なら `daid archive restore asset <名前>` の <名前> を、次の名前に置き換えて（空白や記号を含むときはシェルに合わせて引用符で囲んで）戻してから、改名をやり直してください。" + names;
    }

    /// <summary>
    /// 他の装置の <c>asset_links.json</c> にある子装置の参照（<c>child_assets[].child_asset_name</c>）を
    /// 旧名から新名へ書き換える。子装置は<b>名前で</b>参照されているため、改名すると切れる。
    /// </summary>
    /// <param name="json">asset_links.json の中身</param>
    /// <returns>
    /// 書き換え後の JSON と書き換えた件数。件数 0 または JSON として読めないとき、Json は null。
    /// </returns>
    public static (string? Json, int Count) RenameChildReferences(string json, string oldName, string newName)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return (null, 0);
        }

        if (root is not JsonObject obj) return (null, 0);
        if (obj["child_assets"] is not JsonArray children) return (null, 0);

        var count = 0;
        foreach (var child in children)
        {
            if (child is not JsonObject co) continue;
            if (co["child_asset_name"] is not JsonValue v) continue;
            if (!v.TryGetValue<string>(out var current)) continue;
            if (!string.Equals(current, oldName, StringComparison.OrdinalIgnoreCase)) continue;

            co["child_asset_name"] = newName;
            count++;
        }

        if (count == 0) return (null, 0);

        // asset link / part link が書くときと同じ形（インデントあり・既定のエスケープ）に揃える
        return (obj.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), count);
    }
}
