using DesignAid.Infrastructure.FileSystem;
using DesignAid.Infrastructure.Persistence;
using DesignAid.Infrastructure.VectorSearch;
using Microsoft.EntityFrameworkCore;

namespace DesignAid.Application.Services;

/// <summary>
/// 装置の改名（<c>asset rename</c>）を実行するサービス。
/// <para>
/// <b>ID は変えない。</b>フォルダ・asset.json・他の装置からの参照・DB を同じ処理の中で揃え、
/// 途中で失敗したら、それまでの変更を逆順に戻す。
/// </para>
/// <para>
/// 順序: フォルダ移動 → asset.json → 他の装置の asset_links.json → DB（トランザクション）。
/// DB を最後に置くのは、DB だけがトランザクションで確実に戻せ、ファイルの戻しは
/// 「戻す側が失敗しうる」ため、失敗しやすい操作を先に済ませたいからである。
/// </para>
/// </summary>
public class AssetRenameService
{
    private const string AssetLinksFileName = "asset_links.json";

    private readonly DesignAidDbContext _db;
    private readonly AssetJsonReader _jsonReader = new();

    /// <summary>
    /// 各段階の直後に呼ばれるフック（検査で失敗を注入するための継ぎ目）。本番では null。
    /// 段階名: <c>after-move</c> / <c>after-json</c> / <c>after-links</c> / <c>after-db-save</c>
    /// </summary>
    public Func<string, Task>? StepHook { get; set; }

    /// <summary>
    /// HNSW キャッシュファイルのパス。省略時は <c>&lt;プロジェクト&gt;/hnsw_index.bin</c>。
    /// 文書の索引の行を消したら、コミット後にこのファイルを消す（ロールバック時は消さない）。
    /// </summary>
    public string? HnswIndexPath { get; set; }

    /// <summary>
    /// AssetRenameService を初期化する。
    /// </summary>
    /// <param name="db">DB コンテキスト（呼び出し側が Dispose する）</param>
    public AssetRenameService(DesignAidDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// 装置を改名する。
    /// </summary>
    /// <param name="assetsDir">装置ディレクトリの親（<c>assets/</c>）</param>
    /// <param name="oldName">旧名</param>
    /// <param name="newName">新名</param>
    public async Task<AssetRenameResult> RenameAsync(
        string assetsDir, string oldName, string newName, CancellationToken ct = default)
    {
        // プロジェクト単位の共通ロック（ProjectLock）。事前確認から戻しの完了までを排他する。取れなければ待たずに止める。
        // 共有・ロック違反以外の IO エラー（場所が無い、容量不足など）は、元の例外のまま上へ出す
        var dataDir = Path.GetDirectoryName(Path.GetFullPath(assetsDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))!;
        using var projectLock = ProjectLock.TryAcquire(dataDir);
        if (projectLock == null)
        {
            var busy = new AssetRenameResult { OldName = oldName, NewName = newName };
            busy.Errors.AddRange(AssetRenamePlanner.CheckPreconditions(BusyFacts(oldName, newName)));
            return busy;
        }

        return await RenameLockedAsync(assetsDir, dataDir, oldName, newName, ct);
    }

    private static AssetRenamePlanner.Facts BusyFacts(string oldName, string newName) => new(
        oldName, newName, true, true, false, false, false, false, RenameInProgress: true);

    private async Task<AssetRenameResult> RenameLockedAsync(
        string assetsDir, string dataDir, string oldName, string newName, CancellationToken ct)
    {
        var result = new AssetRenameResult { OldName = oldName, NewName = newName };
        var oldDir = Path.Combine(assetsDir, oldName);
        var newDir = Path.Combine(assetsDir, newName);

        // ---- 1. 事前確認（何も変えない）----
        var nameError = AssetRenamePlanner.ValidateNewName(newName);
        if (nameError != null)
        {
            result.Errors.Add($"新しい名前が使えません: {nameError}");
            return result;
        }

        // 旧名が不正なら、パスを組み立てる前に止める（絶対パスや .. を Path.Combine へ渡さない）
        var oldNameError = AssetRenamePlanner.ValidateName(oldName);
        if (oldNameError != null)
        {
            result.Errors.Add($"旧名が使えません: {oldNameError}");
            return result;
        }
        if (!AssetRenamePlanner.IsDirectChildOf(assetsDir, oldDir))
        {
            result.Errors.Add($"旧名が assets 直下の装置ディレクトリを指していません: {oldName}");
            return result;
        }

        var dbAsset = await _db.Assets.FirstOrDefaultAsync(a => a.Name == oldName, ct);
        var hasJson = Directory.Exists(oldDir) && _jsonReader.Exists(oldDir);
        var json = hasJson ? await _jsonReader.ReadAsync(oldDir, ct) : null;

        var allNames = await _db.Assets.AsNoTracking().Select(a => a.Name).ToListAsync(ct);

        // 大小文字だけ違う名前: asset.json の name、DB の Name、assets/ の別のディレクトリ
        bool Variant(string? n) => n != null && n != oldName && string.Equals(n, oldName, StringComparison.OrdinalIgnoreCase);
        var variants = new List<string>();
        variants.AddRange(allNames.Where(Variant));
        if (Directory.Exists(assetsDir))
            variants.AddRange(Directory.GetDirectories(assetsDir).Select(d => Path.GetFileName(d)).Where(Variant));
        variants = variants.Distinct(StringComparer.Ordinal).ToList();

        // アーカイブ（archive_index.json と archive/assets/*.zip）
        var archived = await IsArchivedAsync(Path.Combine(dataDir, "archive_index.json"), oldName);
        // assets/ 直下の読める別の asset.json の name が新名（大小文字無視）なら、未同期の装置との衝突
        var newJsonOwners = new List<string>();
        if (Directory.Exists(assetsDir))
        {
            foreach (var d in Directory.GetDirectories(assetsDir))
            {
                if (string.Equals(Path.GetFullPath(d), Path.GetFullPath(oldDir), StringComparison.OrdinalIgnoreCase)) continue;
                var other = _jsonReader.Exists(d) ? _jsonReader.Read(d) : null;
                if (other != null && string.Equals(other.Name, newName, StringComparison.OrdinalIgnoreCase))
                    newJsonOwners.Add(Path.GetFileName(d));
            }
        }

        var newArchived = await IsArchivedAsync(Path.Combine(dataDir, "archive_index.json"), newName);

        // 更新対象になる ID（DB の ID と asset.json の ID）は、一致しているかどうかに関係なく、両方とも所有者を照合する。
        // DB の別の行、または assets/ 直下の別の asset.json が同じ ID を持っていれば、変更前に止める
        // （コピーされた asset.json を巻き込んで、別の装置の索引行まで消さないため）。
        // どの装置にも属さない食い違い（旧版の登録）は、所有者がいないので通す
        string? jsonIdOwner = null;
        var targetIdList = new List<Guid>();
        if (dbAsset != null) targetIdList.Add(dbAsset.Id);
        if (json != null && !targetIdList.Contains(json.Id)) targetIdList.Add(json.Id);
        var otherJsons = new List<(string Dir, Guid Id)>();
        if (Directory.Exists(assetsDir))
        {
            foreach (var d in Directory.GetDirectories(assetsDir))
            {
                if (string.Equals(Path.GetFullPath(d), Path.GetFullPath(oldDir), StringComparison.OrdinalIgnoreCase)) continue;
                var other = _jsonReader.Exists(d) ? _jsonReader.Read(d) : null;
                if (other != null) otherJsons.Add((Path.GetFileName(d), other.Id));
            }
        }
        foreach (var id in targetIdList)
        {
            var ownerRow = await _db.Assets.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id && a.Name != oldName, ct);
            if (ownerRow != null) { jsonIdOwner = $"DB の装置 '{ownerRow.Name}'（ID {id}）"; break; }
            var ownerDir = otherJsons.FirstOrDefault(o => o.Id == id);
            if (ownerDir != default) { jsonIdOwner = $"assets/{ownerDir.Dir}（ID {id}）"; break; }
        }

        var (referencing, unreadableArchives) = FindArchivesReferencing(Path.Combine(dataDir, "archive", "assets"), oldName);

        // 新名と大小文字だけ違うもの（assets/ 直下のディレクトリ・ファイル）。大小文字を区別する環境で、別の装置を作ってしまわないため
        var newCollisions = Directory.Exists(assetsDir)
            ? Directory.GetFileSystemEntries(assetsDir).Select(e => Path.GetFileName(e))
                .Where(n => n != newName && string.Equals(n, newName, StringComparison.OrdinalIgnoreCase)).ToList()
            : new List<string>();

        var facts = new AssetRenamePlanner.Facts(
            OldName: oldName,
            NewName: newName,
            OldInDb: dbAsset != null,
            OldHasAssetJson: hasJson,
            OldAssetJsonUnreadable: hasJson && json == null,
            NewInDb: allNames.Any(n => string.Equals(n, newName, StringComparison.OrdinalIgnoreCase)),
            NewHasAssetJson: Directory.Exists(newDir) && _jsonReader.Exists(newDir),
            NewPathExists: Directory.Exists(newDir) || File.Exists(newDir),
            OldDirOutsideAssets: false,
            OldArchived: archived,
            OldCaseVariants: variants,
            ArchivesReferencingOld: referencing,
            UnreadableArchives: unreadableArchives,
            OldAssetJsonName: json?.Name,
            NewFsCaseCollisions: newCollisions,
            NewArchived: newArchived,
            OldJsonIdOwner: jsonIdOwner,
            NewJsonNameOwners: newJsonOwners);

        var errors = AssetRenamePlanner.CheckPreconditions(facts);
        if (errors.Count > 0)
        {
            result.Errors.AddRange(errors);
            return result;
        }

        // asset.json と DB の ID が食い違っていても（0.5.4-alpha 以前の登録）、それぞれの ID は変えない
        if (json != null && dbAsset != null && json.Id != dbAsset.Id)
        {
            result.Warnings.Add(
                $"asset.json の ID ({json.Id}) と DB の ID ({dbAsset.Id}) が食い違っています。どちらの ID も変えていません");
        }

        result.AssetId = json?.Id ?? dbAsset!.Id;

        // 「変更前の名前」は asset.json があればそれを土台にし、無ければ DB を土台にする
        // asset.json に欄が無い（null）ときは DB の履歴を引き継ぐ。明示的な空の配列は asset.json を正とする
        var basePrevious = AssetRenamePlanner.ChoosePreviousNamesBase(json != null, json?.PreviousNames, dbAsset?.PreviousNames);
        var previousNames = AssetRenamePlanner.UpdatePreviousNames(basePrevious, oldName, newName);
        result.PreviousNames = previousNames;

        // ---- 2. 実行（失敗したら逆順に戻す）----
        // Reset は、戻しに成功したときだけ結果のフラグを戻す（戻せなかった段階は「変わったまま」と報告する）
        var undo = new List<(string Description, Func<Task> Action, Action Reset)>();
        try
        {
            // 2-1. フォルダ移動（git の作業ツリーと .git ごと）
            if (Directory.Exists(oldDir))
            {
                try
                {
                    Directory.Move(oldDir, newDir);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // 何も変わっていない。別のプロセスがフォルダ内を開いていると Windows ではここで失敗する
                    throw new AssetRenameStepException(
                        $"フォルダを移動できません（別のプロセスが assets/{oldName} 内のファイルやフォルダを開いている可能性があります）: {ex.Message}", ex);
                }

                result.FolderMoved = true;
                undo.Add(("フォルダの移動", async () =>
                {
                    // 移した直後は Windows のウイルス対策・インデクサが一時的にフォルダを掴むことがあるため、数回やり直す
                    for (var attempt = 1; ; attempt++)
                    {
                        try
                        {
                            Directory.Move(newDir, oldDir);
                            return;
                        }
                        catch (Exception ex) when (attempt < 5 && ex is IOException or UnauthorizedAccessException)
                        {
                            await Task.Delay(200 * attempt);
                        }
                    }
                }, () => result.FolderMoved = false));
                await RunHookAsync("after-move");
            }

            // 2-2. asset.json
            if (json != null)
            {
                var jsonPath = Path.Combine(newDir, "asset.json");
                var original = await File.ReadAllBytesAsync(jsonPath, ct);
                var updated = json with
                {
                    Name = newName,
                    DisplayName = AssetRenamePlanner.ResolveDisplayName(json.DisplayName, oldName, newName)
                                  ?? json.DisplayName,
                    PreviousNames = previousNames
                };
                undo.Add(("asset.json の書き換え", () => File.WriteAllBytesAsync(jsonPath, original, CancellationToken.None),
                    () => result.AssetJsonUpdated = false));
                await _jsonReader.WriteAsync(newDir, updated, ct);
                result.AssetJsonUpdated = true;
                result.DisplayName = updated.DisplayName;
                await RunHookAsync("after-json");
            }
            else if (Directory.Exists(newDir))
            {
                result.Warnings.Add(
                    "移動先に asset.json がありません（DB のみの装置）。復元するには daid sync --restore-json を実行してください");
            }

            // 2-3. 他の装置の asset_links.json にある子装置の参照（名前で結ばれている）
            if (Directory.Exists(assetsDir))
            {
                foreach (var dir in Directory.GetDirectories(assetsDir).OrderBy(d => d, StringComparer.Ordinal))
                {
                    if (string.Equals(Path.GetFullPath(dir), Path.GetFullPath(newDir), StringComparison.OrdinalIgnoreCase))
                        continue;

                    var linksPath = Path.Combine(dir, AssetLinksFileName);
                    if (!File.Exists(linksPath)) continue;

                    var originalBytes = await File.ReadAllBytesAsync(linksPath, ct);
                    // BOM 付きでも読めるよう、文字列化は File の読み込みに任せる
                    var text = await File.ReadAllTextAsync(linksPath, ct);
                    var (newText, count) = AssetRenamePlanner.RenameChildReferences(text, oldName, newName);
                    if (newText == null)
                    {
                        // 読めない JSON に旧名が書かれていても気づけないので、旧名を含むなら警告する
                        if (count == 0 && text.Contains(oldName, StringComparison.OrdinalIgnoreCase)
                            && !IsParsable(text))
                        {
                            result.Warnings.Add(
                                $"{Path.GetFileName(dir)}/{AssetLinksFileName} は JSON として読めず、旧名を含みます。手で確認してください");
                        }
                        continue;
                    }

                    var linkLabel = $"{Path.GetFileName(dir)}/{AssetLinksFileName}";
                    undo.Add(($"{linkLabel} の書き換え",
                        () => File.WriteAllBytesAsync(linksPath, originalBytes, CancellationToken.None),
                        () => result.LinkFilesUpdated.Remove(linkLabel)));
                    await File.WriteAllTextAsync(linksPath, newText, ct);
                    result.LinkFilesUpdated.Add(linkLabel);
                }
            }
            await RunHookAsync("after-links");

            // 2-4. DB（トランザクション。ID は変えない）
            // Assets の行が無い（asset.json だけの）装置でも、asset.json の ID の文書索引は更新する。Assets の行は作らない
            if (dbAsset != null || json != null)
            {
                await using var tx = await _db.Database.BeginTransactionAsync(ct);
                try
                {
                    if (dbAsset != null)
                    {
                        dbAsset.Rename(
                            newName,
                            newDir,
                            AssetRenamePlanner.ResolveDisplayName(dbAsset.DisplayName, oldName, newName),
                            previousNames);
                        result.DisplayName ??= dbAsset.DisplayName;
                    }

                    // ベクトル索引は装置を名前とパスで持っている（ID でも引けるが、表示と検索の元は名前）
                    // 文書索引は asset.json の ID で登録される（CollectAssetDocPoints）ので、DB の ID と asset.json の ID の両方を対象にする。
                    // それ以外の行（別の装置・アーカイブ済み）は巻き込まない
                    var targetIds = new List<string>();
                    if (dbAsset != null) targetIds.Add(dbAsset.Id.ToString());
                    if (json != null && json.Id != dbAsset?.Id) targetIds.Add(json.Id.ToString());
                    var vectorRows = await _db.VectorIndex.Where(v => v.AssetId != null && targetIds.Contains(v.AssetId)).ToListAsync(ct);
                    // 文書チャンク（asset_doc）の行は<b>消す</b>。PartId が旧名から作られる（SyncCommand.DocChunkId）ため、
                    // 書き換えて残すと、旧名を再利用した別の装置の同期が、この行を上書きしてしまう。
                    // 次の sync --include-vectors が新しい名前のキーで作り直す。
                    // 部品（spec）の行の PartId は部品の ID で、名前から作られないので、装置名だけを更新する
                    var docRows = vectorRows.Where(v => v.Type == "asset_doc").ToList();
                    _db.VectorIndex.RemoveRange(docRows);
                    foreach (var v in vectorRows.Where(v => v.Type != "asset_doc"))
                    {
                        v.AssetName = newName;
                    }
                    result.VectorRowsUpdated = vectorRows.Count;

                    await _db.SaveChangesAsync(ct);
                    await RunHookAsync("after-db-save");
                    await tx.CommitAsync(ct);
                    result.DbUpdated = dbAsset != null;

                    // コミットの後で、検索のキャッシュを無効にする（行を消したので、古いグラフが残ると
                    // 消えた文書が検索の上位を占めて、残っている別の装置の結果まで欠ける）。
                    // 起動中のインスタンスは、次の検索で VectorIndex の状態の変化に気づいて作り直す。
                    // ロールバックしたときは行が変わっていないので、ここへは来ない
                    if (docRows.Count > 0)
                    {
                        var cachePath = HnswIndexPath ?? Path.Combine(dataDir, "hnsw_index.bin");
                        if (!VectorSearchService.DeleteCacheFile(cachePath))
                        {
                            result.Warnings.Add(
                                $"検索のキャッシュファイルを消せませんでした: {cachePath}。構築時の判定値が今の索引と一致しないので次の検索では使われませんが、手で消してください");
                        }
                    }
                }
                catch
                {
                    await tx.RollbackAsync(CancellationToken.None);
                    // 追跡中の変更を捨てる（同じコンテキストを使い続けても戻った状態が見えるように）
                    foreach (var entry in _db.ChangeTracker.Entries().ToList())
                        entry.State = EntityState.Detached;
                    throw;
                }
            }

            if (dbAsset == null)
            {
                result.Warnings.Add("DB に行がありません（asset.json のみの装置）。DB へ登録するには daid sync を実行してください");
            }

            result.Success = true;
            result.NewPath = Path.GetFullPath(newDir);
            return result;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Failure = ex is AssetRenameStepException ? ex.Message : $"改名に失敗しました: {ex.Message}";

            // 逆順に戻す
            for (var i = undo.Count - 1; i >= 0; i--)
            {
                try
                {
                    await undo[i].Action();
                    undo[i].Reset();
                    result.RolledBack.Add(undo[i].Description);
                }
                catch (Exception undoEx)
                {
                    result.RollbackFailures.Add($"{undo[i].Description} を戻せませんでした: {undoEx.Message}");
                }
            }

            // DB はトランザクションで、コミット（DbUpdated = true）の後に失敗する処理は無い
            result.DbUpdated = false;
            return result;
        }
    }

    /// <summary>archive_index.json に、この名前の装置がアーカイブ済みとして載っているか。</summary>
    private static async Task<bool> IsArchivedAsync(string indexPath, string name)
    {
        var index = await new ArchiveIndexReader().LoadAsync(indexPath);
        return index.Archived.Any(e => e.Type == "asset"
            && string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// アーカイブ（zip）の中の asset_links.json が、この名前を子装置として参照している親装置の名前。
    /// 参照の無いアーカイブは含めない。読めない zip は飛ばす。
    /// </summary>
    private static (List<string> Referencing, List<string> Unreadable) FindArchivesReferencing(
        string archiveAssetsDir, string name)
    {
        var found = new List<string>();
        var unreadable = new List<string>();
        if (!Directory.Exists(archiveAssetsDir)) return (found, unreadable);

        foreach (var zip in Directory.GetFiles(archiveAssetsDir, "*.zip").OrderBy(z => z, StringComparer.Ordinal))
        {
            try
            {
                using var archive = System.IO.Compression.ZipFile.OpenRead(zip);
                var entry = archive.Entries.FirstOrDefault(e =>
                    string.Equals(e.FullName, AssetLinksFileName, StringComparison.OrdinalIgnoreCase));
                if (entry == null) continue;
                using var reader = new StreamReader(entry.Open());
                var text = reader.ReadToEnd();
                var (_, count) = AssetRenamePlanner.RenameChildReferences(text, name, name + "_");
                if (count > 0) found.Add(Path.GetFileNameWithoutExtension(zip));
                else if (text.Contains(name, StringComparison.OrdinalIgnoreCase) && !IsParsable(text))
                    unreadable.Add(zip); // 読めない JSON に旧名が書かれている
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                           or InvalidDataException or NotSupportedException)
            {
                // 検査できないアーカイブは「参照なし」とみなさない。改名を止める理由にする
                unreadable.Add(zip);
            }
        }
        return (found, unreadable);
    }

    private Task RunHookAsync(string step) => StepHook?.Invoke(step) ?? Task.CompletedTask;

    private static bool IsParsable(string text)
    {
        try
        {
            System.Text.Json.JsonDocument.Parse(text).Dispose();
            return true;
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }

    /// <summary>手順の途中で、原因が利用者に分かる形で止めるための例外（メッセージをそのまま見せる）。</summary>
    private sealed class AssetRenameStepException : Exception
    {
        public AssetRenameStepException(string message, Exception inner) : base(message, inner) { }
    }
}

/// <summary>
/// 改名の結果。
/// </summary>
public class AssetRenameResult
{
    /// <summary>旧名</summary>
    public string OldName { get; init; } = string.Empty;

    /// <summary>新名</summary>
    public string NewName { get; init; } = string.Empty;

    /// <summary>成功したか</summary>
    public bool Success { get; set; }

    /// <summary>事前確認で止めた理由（何も変えていない）</summary>
    public List<string> Errors { get; } = new();

    /// <summary>実行中の失敗の理由（Success=false かつ Errors が空のとき）</summary>
    public string? Failure { get; set; }

    /// <summary>失敗時に戻せた段階</summary>
    public List<string> RolledBack { get; } = new();

    /// <summary>失敗時に戻せなかった段階（これが空でなければ手で確認が要る）</summary>
    public List<string> RollbackFailures { get; } = new();

    /// <summary>警告</summary>
    public List<string> Warnings { get; } = new();

    /// <summary>装置 ID（変わらない）</summary>
    public Guid AssetId { get; set; }

    /// <summary>改名後のフォルダ（絶対パス）</summary>
    public string? NewPath { get; set; }

    /// <summary>改名後の表示名</summary>
    public string? DisplayName { get; set; }

    /// <summary>改名後の「変更前の名前」一覧</summary>
    public List<string> PreviousNames { get; set; } = new();

    /// <summary>フォルダを移したか</summary>
    public bool FolderMoved { get; set; }

    /// <summary>asset.json を書き換えたか</summary>
    public bool AssetJsonUpdated { get; set; }

    /// <summary>DB を更新したか</summary>
    public bool DbUpdated { get; set; }

    /// <summary>更新したベクトル索引の行数</summary>
    public int VectorRowsUpdated { get; set; }

    /// <summary>参照を書き換えた他の装置の asset_links.json</summary>
    public List<string> LinkFilesUpdated { get; } = new();
}
