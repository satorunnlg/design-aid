using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
using System.Text.Json;
using DesignAid.Application.Services;

namespace DesignAid.Commands.Asset;

/// <summary>
/// 装置を改名するコマンド。
/// <b>ID は変えず</b>、フォルダ・asset.json・他の装置からの参照・DB の名前とパスを揃える。
/// </summary>
public class AssetRenameCommand : Command
{
    public AssetRenameCommand() : base("rename", "装置を改名（ID は変えずに名前とフォルダを変える）")
    {
        this.Add(new Argument<string>("old-name", "現在の装置名"));
        this.Add(new Argument<string>("new-name", "新しい装置名"));
        this.Add(new Option<bool>("--json", "JSON形式で出力"));
        this.Add(new Option<bool>(new[] { "--quiet", "-q" }, "成功時の出力を省く（エラー・警告は stderr に出る）"));

        this.Handler = CommandHandler.Create<string, string, bool, bool>(ExecuteAsync);
    }

    private static readonly JsonSerializerOptions JsonOut = new() { WriteIndented = true };

    /// <summary>
    /// 終了コードを<b>戻り値で</b>返す。System.CommandLine の呼び出しは Task のハンドラの
    /// <c>Environment.ExitCode</c> を拾わず 0 を返すため、失敗の終了コード（1 など）が
    /// 呼び出し側に届くよう Task&lt;int&gt; にしている。
    /// </summary>
    private static async Task<int> ExecuteAsync(string oldName, string newName, bool json, bool quiet)
    {
        if (CommandHelper.EnsureDataDirectory() == null)
        {
            // プロジェクトが見つからない。--json のときも、標準出力に失敗の JSON を 1 つだけ出す（診断は stderr）
            if (json) WriteFailureJson(oldName, newName, new[] { "プロジェクトが見つかりません（design_aid.db を探しましたが見つかりませんでした）" });
            return Environment.ExitCode;
        }
        var assetsDir = CommandHelper.GetAssetsDirectory();

        AssetRenameResult result;
        try
        {
            using var db = CommandHelper.CreateDbContext();
            var settings = CommandHelper.LoadSettings();
            var hnswPath = Path.Combine(CommandHelper.GetDataDirectory()!,
                settings.Get("vector_search.hnsw_index_path", "hnsw_index.bin")!);
            result = await new AssetRenameService(db) { HnswIndexPath = hnswPath }.RenameAsync(assetsDir, oldName, newName);
        }
        catch (Exception ex)
        {
            // DB を開けないなど。変更は事前確認の後にしか始まらないので、何も変えていない
            // DB を開けない・読み取り権限が無い・archive_index.json が壊れている・ロックを取れない など
            Console.Error.WriteLine($"[ERROR] 改名を始められませんでした（何も変更していません）: {ex.Message}");
            if (json) WriteFailureJson(oldName, newName, new[] { $"改名を始められませんでした（何も変更していません）: {ex.Message}" });
            Environment.ExitCode = 1;
            return 1;
        }

        if (!result.Success)
        {
            ReportFailure(result, json);
            Environment.ExitCode = 1;
            return 1;
        }

        // 再実行を勧めるもの（名前やパスで結ばれていて、この処理では直せないもの）
        var followUps = new List<string>();
        if (result.VectorRowsUpdated > 0)
        {
            followUps.Add("daid sync --include-vectors");
        }

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                success = true,
                id = result.AssetId,
                oldName = result.OldName,
                newName = result.NewName,
                path = result.NewPath,
                displayName = result.DisplayName,
                previousNames = result.PreviousNames,
                folderMoved = result.FolderMoved,
                assetJsonUpdated = result.AssetJsonUpdated,
                dbUpdated = result.DbUpdated,
                vectorRowsUpdated = result.VectorRowsUpdated,
                linkFilesUpdated = result.LinkFilesUpdated,
                warnings = result.Warnings,
                followUps,
                gitRemoteChanged = false
            }, JsonOut));
        }
        else if (!quiet)
        {
            Console.WriteLine($"Asset renamed: {result.OldName} -> {result.NewName}");
            Console.WriteLine($"  ID: {result.AssetId}（変わりません）");
            Console.WriteLine($"  Path: {result.NewPath}");
            Console.WriteLine($"  PreviousNames: {string.Join(", ", result.PreviousNames)}");
            if (result.LinkFilesUpdated.Count > 0)
            {
                Console.WriteLine($"  子装置の参照を書き換えた: {string.Join(", ", result.LinkFilesUpdated)}");
            }
            if (result.VectorRowsUpdated > 0)
            {
                Console.WriteLine($"  ベクトル索引: {result.VectorRowsUpdated} 行を更新または削除（文書チャンクは消えるので再作成が要ります）");
            }
            foreach (var w in result.Warnings)
            {
                Console.WriteLine($"  [WARN] {w}");
            }
            foreach (var f in followUps)
            {
                Console.WriteLine($"  再実行を推奨: {f}（検索用の埋め込みには旧名が含まれているため）");
            }
            Console.WriteLine("  Git: リモートの名前・URL は変えていません（必要なら git remote と GitHub 側で手動で変更してください）");
        }

        // --quiet / --json のときは警告を stderr へ出す（stdout を汚さない）
        if (quiet || json)
        {
            foreach (var w in result.Warnings)
            {
                Console.Error.WriteLine($"[WARN] {w}");
            }
        }

        return 0;
    }

    /// <summary>
    /// 失敗を報告する。事前確認で止めた場合は「何も変更していません」、
    /// 実行中の失敗では戻した段階と、戻せなかった段階を出す。
    /// </summary>
    private static void ReportFailure(AssetRenameResult result, bool json)
    {
        var lines = new List<string>();
        if (result.Errors.Count > 0)
        {
            lines.AddRange(result.Errors.Select(e => $"[ERROR] {e}"));
            lines.Add("何も変更していません。");
        }
        else
        {
            lines.Add($"[ERROR] {result.Failure}");
            if (result.RolledBack.Count > 0)
            {
                lines.Add($"戻した: {string.Join("、", result.RolledBack)}");
            }
            if (result.RollbackFailures.Count > 0)
            {
                lines.AddRange(result.RollbackFailures.Select(m => $"[ERROR] {m}"));
                lines.Add("一部が戻っていません。assets/ と asset.json の状態を確認してください。");
            }
            else
            {
                lines.Add("変更は元に戻しました。");
            }
        }

        foreach (var l in lines) Console.Error.WriteLine(l);

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                success = false,
                oldName = result.OldName,
                newName = result.NewName,
                errors = result.Errors.Count > 0 ? result.Errors : new List<string> { result.Failure ?? "" },
                rolledBack = result.RolledBack,
                rollbackFailures = result.RollbackFailures
            }, JsonOut));
        }
    }

    /// <summary>
    /// 改名の前の段階（プロジェクトが無い・初期化の失敗）の失敗を、--json の失敗応答と同じ形で標準出力へ出す。
    /// </summary>
    private static void WriteFailureJson(string oldName, string newName, IEnumerable<string> errors)
    {
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            success = false,
            oldName,
            newName,
            errors = errors.ToList(),
            rolledBack = new List<string>(),
            rollbackFailures = new List<string>()
        }, JsonOut));
    }
}
