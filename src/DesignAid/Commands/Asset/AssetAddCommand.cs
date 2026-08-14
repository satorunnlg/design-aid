using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
using System.Diagnostics;
using DesignAid.Domain.Entities;
using DesignAid.Infrastructure.FileSystem;
using DesignAid.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

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

        // **asset.json と DB で同じ ID を使う**（Issue #2）。
        // そのために DB を先に見る。既に行があればその ID を引き継ぐ。
        // これは「DB には行があるが asset.json が無い」装置を復旧する経路でもある（Issue #1）。
        DesignAidDbContext? db = null;
        Domain.Entities.Asset? existing = null;
        try
        {
            db = CommandHelper.CreateDbContext();
            existing = await db.Assets.FirstOrDefaultAsync(a => a.Name == name);
        }
        catch (Exception ex)
        {
            // DB が読めなくても asset.json は作る（従来の挙動を維持する）
            Console.Error.WriteLine($"[WARN] DB を参照できませんでした（JSON のみ作成します）: {ex.Message}");
        }

        var assetId = existing?.Id ?? Guid.NewGuid();
        var reusedDbRow = existing != null;

        try
        {
            // asset.json を作成
            await assetJsonReader.CreateAsync(assetPath, assetId, name, displayName ?? name, description ?? "");

            // DB にも登録（既存行があれば触らない）
            if (db != null && existing == null)
            {
                try
                {
                    var asset = Domain.Entities.Asset.Create(
                        name, assetPath, displayName ?? name, description ?? "", assetId);
                    db.Assets.Add(asset);
                    await db.SaveChangesAsync();
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[WARN] DB 登録に失敗しました（JSON は作成済み）: {ex.Message}");
                }
            }
        }
        finally
        {
            db?.Dispose();
        }

        // Git リポジトリを初期化（デフォルト）
        var gitInitialized = false;
        if (!noGit)
        {
            gitInitialized = await InitializeGitRepositoryAsync(assetPath);
        }

        Console.WriteLine();
        Console.WriteLine(reusedDbRow ? $"Asset json restored: {name}" : $"Asset created: {name}");
        Console.WriteLine($"  Path: {assetPath}");
        Console.WriteLine($"  ID: {assetId}");
        if (reusedDbRow)
        {
            Console.WriteLine("  DB: 既存の行を再利用しました（ID を引き継ぎ、DB は変更していません）");
        }
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
