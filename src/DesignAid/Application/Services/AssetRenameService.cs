using DesignAid.Infrastructure.FileSystem;
using DesignAid.Infrastructure.Persistence;
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
        var result = new AssetRenameResult { OldName = oldName, NewName = newName };
        var oldDir = Path.Combine(assetsDir, oldName);
        var newDir = Path.Combine(assetsDir, newName);

        // ---- 1. 事前確認（何も変えない）----
        var nameError = AssetRenamePlanner.ValidateName(newName);
        if (nameError != null)
        {
            result.Errors.Add($"新しい名前が使えません: {nameError}");
            return result;
        }

        var dbAsset = await _db.Assets.FirstOrDefaultAsync(a => a.Name == oldName, ct);
        var hasJson = Directory.Exists(oldDir) && _jsonReader.Exists(oldDir);
        var json = hasJson ? await _jsonReader.ReadAsync(oldDir, ct) : null;

        var allNames = await _db.Assets.AsNoTracking().Select(a => a.Name).ToListAsync(ct);
        var facts = new AssetRenamePlanner.Facts(
            OldName: oldName,
            NewName: newName,
            OldInDb: dbAsset != null,
            OldHasAssetJson: hasJson,
            OldAssetJsonUnreadable: hasJson && json == null,
            NewInDb: allNames.Any(n => string.Equals(n, newName, StringComparison.OrdinalIgnoreCase)),
            NewHasAssetJson: Directory.Exists(newDir) && _jsonReader.Exists(newDir),
            NewPathExists: Directory.Exists(newDir) || File.Exists(newDir));

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
        var basePrevious = json != null ? json.PreviousNames : dbAsset!.PreviousNames;
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
                undo.Add(("フォルダの移動", () =>
                {
                    Directory.Move(newDir, oldDir);
                    return Task.CompletedTask;
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
            if (dbAsset != null)
            {
                await using var tx = await _db.Database.BeginTransactionAsync(ct);
                try
                {
                    var idText = dbAsset.Id.ToString();
                    dbAsset.Rename(
                        newName,
                        newDir,
                        AssetRenamePlanner.ResolveDisplayName(dbAsset.DisplayName, oldName, newName),
                        previousNames);
                    result.DisplayName ??= dbAsset.DisplayName;

                    // ベクトル索引は装置を名前とパスで持っている（ID でも引けるが、表示と検索の元は名前）
                    var vectorRows = await _db.VectorIndex.Where(v => v.AssetId == idText).ToListAsync(ct);
                    foreach (var v in vectorRows)
                    {
                        v.AssetName = newName;
                        if (v.Type == "asset_doc")
                        {
                            v.PartNumber = AssetRenamePlanner.RenameDocPartNumber(v.PartNumber, oldName, newName);
                            if (v.FilePath != null)
                                v.FilePath = AssetRenamePlanner.RenameDocFilePath(v.FilePath, oldName, newName);
                        }
                    }
                    result.VectorRowsUpdated = vectorRows.Count;

                    await _db.SaveChangesAsync(ct);
                    await RunHookAsync("after-db-save");
                    await tx.CommitAsync(ct);
                    result.DbUpdated = true;
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
            else
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
