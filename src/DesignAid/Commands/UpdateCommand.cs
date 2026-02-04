using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
using System.Diagnostics;
using System.Reflection;

namespace DesignAid.Commands;

/// <summary>
/// ツールを最新版に更新するコマンド。
/// </summary>
public class UpdateCommand : Command
{
    public UpdateCommand() : base("update", "ツールを最新版に更新")
    {
        this.Add(new Option<bool>("--check", "更新があるか確認のみ（更新しない）"));
        this.Add(new Option<string?>("--source", "パッケージソース（省略時は NuGet.org）"));

        this.Handler = CommandHandler.Create<bool, string?>(Execute);
    }

    private static void Execute(bool check, string? source)
    {
        var currentVersion = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "unknown";

        Console.WriteLine($"現在のバージョン: {currentVersion}");

        if (check)
        {
            Console.WriteLine();
            Console.WriteLine("更新確認中...");
            // dotnet tool search で最新バージョンを確認
            var searchArgs = "tool search DesignAid --take 1";
            if (!string.IsNullOrEmpty(source))
            {
                searchArgs += $" --source \"{source}\"";
            }

            var searchPsi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = searchArgs,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            try
            {
                var process = Process.Start(searchPsi);
                if (process != null)
                {
                    var output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();
                    Console.WriteLine(output);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ERROR] バージョン確認に失敗しました: {ex.Message}");
            }
            return;
        }

        Console.WriteLine();
        Console.WriteLine("更新を実行中...");

        var updateArgs = "tool update -g DesignAid";
        if (!string.IsNullOrEmpty(source))
        {
            updateArgs += $" --add-source \"{source}\"";
        }

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = updateArgs,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        try
        {
            var process = Process.Start(psi);
            if (process != null)
            {
                var stdout = process.StandardOutput.ReadToEnd();
                var stderr = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (!string.IsNullOrWhiteSpace(stdout))
                    Console.WriteLine(stdout);
                if (!string.IsNullOrWhiteSpace(stderr))
                    Console.Error.WriteLine(stderr);

                if (process.ExitCode == 0)
                {
                    Console.WriteLine("更新が完了しました。");
                }
                else
                {
                    Console.Error.WriteLine($"[ERROR] 更新に失敗しました（終了コード: {process.ExitCode}）");
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ERROR] 更新の実行に失敗しました: {ex.Message}");
        }
    }
}
