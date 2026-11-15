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
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
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

        // Path.GetInvalidFileNameChars は OS で内容が違う（Unix では '/' と NUL しか返さない）ので使わず、
        // どこにクローンしても同じ結果になるよう Windows の禁止文字を固定で持つ（Unix の禁止文字も含まれる）。
        const string forbidden = "<>:\"/\\|?*";
        foreach (var c in name)
        {
            if (c < ' ' || forbidden.Contains(c))
                return $"装置名に使えない文字が含まれています: '{(c < ' ' ? $"U+{(int)c:X4}" : c.ToString())}'";
        }

        // CON.txt のように拡張子が付いても予約名になる
        var stem = name.Split('.')[0];
        if (ReservedNames.Contains(stem))
            return $"'{name}' は OS の予約名のため使えません";

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
        IReadOnlyList<string>? ArchivesReferencingOld = null);

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

        var nameError = ValidateName(f.NewName);
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

        if (f.OldArchived)
            errors.Add($"改名元の装置はアーカイブ済みです: {f.OldName}（先に daid archive restore asset {f.OldName} で復元してください）");

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

    /// <summary>
    /// ベクトル索引の文書チャンクの表示名 <c>[DOC] 旧名/ファイル</c> を新名へ置き換える。
    /// 該当しなければそのまま返す。
    /// </summary>
    public static string RenameDocPartNumber(string partNumber, string oldName, string newName)
    {
        var prefix = $"[DOC] {oldName}/";
        return partNumber.StartsWith(prefix, StringComparison.Ordinal)
            ? $"[DOC] {newName}/" + partNumber[prefix.Length..]
            : partNumber;
    }

    /// <summary>
    /// ベクトル索引の文書チャンクのファイルパス <c>assets/旧名/ファイル</c> を新名へ置き換える。
    /// 該当しなければそのまま返す。
    /// </summary>
    public static string RenameDocFilePath(string filePath, string oldName, string newName)
    {
        var prefix = $"assets/{oldName}/";
        return filePath.StartsWith(prefix, StringComparison.Ordinal)
            ? $"assets/{newName}/" + filePath[prefix.Length..]
            : filePath;
    }
}
