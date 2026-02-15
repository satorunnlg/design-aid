using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
using System.Diagnostics;
using DesignAid.Infrastructure.FileSystem;

namespace DesignAid.Commands.Asset;

/// <summary>
/// 装置を追加するコマンド。
/// </summary>
public class AssetAddCommand : Command
{
    public AssetAddCommand() : base("add", "装置を追加")
    {
        this.Add(new Argument<string>("name", "装置名"));
        this.Add(new Option<string?>("--display-name", "表示名"));
        this.Add(new Option<string?>("--description", "説明"));
        this.Add(new Option<bool>("--no-git", "Git リポジトリを初期化しない"));

        this.Handler = CommandHandler.Create<string, string?, string?, bool>(ExecuteAsync);
    }

    private static async Task ExecuteAsync(string name, string? displayName, string? description, bool noGit)
    {
        if (CommandHelper.EnsureDataDirectory() == null) return;
        var assetsDir = CommandHelper.GetAssetsDirectory();
        var assetPath = Path.Combine(assetsDir, name);

        var assetJsonReader = new AssetJsonReader();
        if (Directory.Exists(assetPath) && assetJsonReader.Exists(assetPath))
        {
            Console.Error.WriteLine($"[ERROR] 装置は既に存在します: {name}");
            Environment.ExitCode = 1;
            return;
        }

        // 装置ディレクトリを作成
        Directory.CreateDirectory(assetPath);

        // asset.json を作成
        var assetId = Guid.NewGuid();
        await assetJsonReader.CreateAsync(assetPath, assetId, name, displayName ?? name, description ?? "");

        // Git リポジトリを初期化（デフォルト）
        var gitInitialized = false;
        if (!noGit)
        {
            gitInitialized = await InitializeGitRepositoryAsync(assetPath);
        }

        Console.WriteLine();
        Console.WriteLine($"Asset created: {name}");
        Console.WriteLine($"  Path: {assetPath}");
        Console.WriteLine($"  ID: {assetId}");
        if (gitInitialized)
        {
            Console.WriteLine($"  Git: initialized");
        }
        else if (!noGit)
        {
            Console.WriteLine($"  Git: initialization failed (git not found?)");
        }
    }

    /// <summary>
    /// 指定されたパスで Git リポジトリを初期化する。
    /// </summary>
    private static async Task<bool> InitializeGitRepositoryAsync(string path)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "init",
                WorkingDirectory = path,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null) return false;

            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
