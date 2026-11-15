using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text;
using System.Text.Json;

namespace DesignAid.Commands;

/// <summary>
/// <c>--json</c> を持つコマンドの共通の失敗処理（#10）。
/// <para>
/// 契約: <c>--json</c> のとき、標準出力は<b>常に有効な JSON 1 つ</b>になる。成功ならコマンドの出力のまま、
/// 失敗（例外・0 以外の終了コード）でコマンドが JSON を出していなければ、失敗の JSON
/// <c>{ "success": false, "errors": [...], "exitCode": N }</c> を 1 つだけ出す。診断（人が読む文）は標準エラーに出る。
/// </para>
/// <para>
/// 標準出力を一度バッファへ受けて、終わってから判定して書き出す（JSON は 1 つの文書なので、途中の出力を急ぐ必要が無い）。
/// 判定は純関数 <see cref="ResolveStdout"/> に切り出してある。
/// </para>
/// </summary>
public static class JsonGuard
{
    /// <summary>
    /// コマンドのハンドラを包む。<c>--json</c> のオプションを持たないコマンドには何もしない。
    /// 戻り値の終了コードは、ハンドラの戻り値が 0 のとき <see cref="Environment.ExitCode"/> を採る
    /// （<c>Environment.ExitCode</c> を設定するだけで戻り値を返さないハンドラが、失敗しても 0 になるのを防ぐ。#8）。
    /// </summary>
    public static ICommandHandler Wrap(Command command, ICommandHandler inner)
    {
        var jsonOption = command.Options.FirstOrDefault(o => o.HasAlias("--json")) as Option<bool>;
        return jsonOption == null ? inner : new GuardedHandler(jsonOption, inner);
    }

    /// <summary>
    /// 標準出力に実際に書く内容を決める（純関数）。
    /// </summary>
    /// <param name="stdout">コマンドが標準出力へ書いた内容</param>
    /// <param name="stderrLines">コマンドが標準エラーへ書いた行（失敗の JSON の errors の材料）</param>
    /// <param name="exitCode">終了コード</param>
    /// <param name="exceptionMessage">例外で終わったときの文。例外が無ければ null</param>
    public static string ResolveStdout(string stdout, IReadOnlyList<string> stderrLines, int exitCode, string? exceptionMessage)
    {
        var failed = exceptionMessage != null || exitCode != 0;
        if (!failed) return stdout;

        // 例外のときは、途中まで書いた出力を信用しない（成功の JSON を出してから落ちた場合も含む）。
        // 0 以外の終了コードで、コマンド自身が有効な JSON を 1 つ出しているなら（検査の結果など）そのまま使う
        if (exceptionMessage == null && IsSingleJson(stdout)) return stdout;

        var errors = new List<string>();
        foreach (var line in stderrLines)
        {
            var text = line.Trim();
            if (text.Length == 0) continue;
            errors.Add(text.StartsWith("[ERROR]", StringComparison.Ordinal) ? text["[ERROR]".Length..].Trim() : text);
        }
        if (exceptionMessage != null && !errors.Contains(exceptionMessage)) errors.Add(exceptionMessage);
        if (errors.Count == 0) errors.Add($"コマンドが失敗しました（終了コード {exitCode}）");

        return BuildFailureJson(errors, exitCode) + Environment.NewLine;
    }

    /// <summary>失敗の JSON を組み立てる。</summary>
    public static string BuildFailureJson(IEnumerable<string> errors, int exitCode)
        => JsonSerializer.Serialize(new { success = false, errors = errors.ToList(), exitCode },
            new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

    /// <summary>テキスト全体が、有効な JSON の値 1 つ（前後の空白だけ許す）であるか。</summary>
    public static bool IsSingleJson(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        try
        {
            using var _ = JsonDocument.Parse(text);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private sealed class GuardedHandler(Option<bool> jsonOption, ICommandHandler inner) : ICommandHandler
    {
        public int Invoke(InvocationContext context) => InvokeAsync(context).GetAwaiter().GetResult();

        public async Task<int> InvokeAsync(InvocationContext context)
        {
            if (!context.ParseResult.GetValueForOption(jsonOption))
            {
                var code0 = await inner.InvokeAsync(context);
                return code0 != 0 ? code0 : Environment.ExitCode;
            }

            var originalOut = Console.Out;
            var originalErr = Console.Error;
            var outBuffer = new StringWriter();
            var errBuffer = new StringWriter();
            Console.SetOut(outBuffer);
            Console.SetError(new TeeWriter(originalErr, errBuffer));

            int code;
            string? exceptionMessage = null;
            try
            {
                code = await inner.InvokeAsync(context);
                if (code == 0) code = Environment.ExitCode;
            }
            catch (Exception caught)
            {
                // リフレクションの呼び出し（NamingConventionBinder）が包んだ例外は、中身を報告する
                var ex = caught is System.Reflection.TargetInvocationException { InnerException: { } inner } ? inner : caught;
                exceptionMessage = $"{ex.GetType().Name}: {ex.Message}";
                // 診断は標準エラーへ（人が読む）。標準出力には失敗の JSON だけが出る
                Console.Error.WriteLine($"[ERROR] {exceptionMessage}");
                code = 1;
            }
            finally
            {
                Console.SetOut(originalOut);
                Console.SetError(originalErr);
            }

            var lines = errBuffer.ToString().Split('\n');
            var resolved = ResolveStdout(outBuffer.ToString(), lines, code, exceptionMessage);
            originalOut.Write(resolved);
            originalOut.Flush();
            if (code != 0) Environment.ExitCode = code;
            return code;
        }
    }

    /// <summary>書いた内容を元の書き込み先へ流しつつ、控えも取る。</summary>
    private sealed class TeeWriter(TextWriter first, TextWriter second) : TextWriter
    {
        public override Encoding Encoding => first.Encoding;
        public override void Write(char value) { first.Write(value); second.Write(value); }
        public override void Write(string? value) { first.Write(value); second.Write(value); }
        public override void Write(char[] buffer, int index, int count) { first.Write(buffer, index, count); second.Write(buffer, index, count); }
        public override void Flush() { first.Flush(); second.Flush(); }
    }
}
