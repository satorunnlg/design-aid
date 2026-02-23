using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
using System.Diagnostics;
using DesignAid.Domain.Entities;
using DesignAid.Domain.ValueObjects;
using DesignAid.Infrastructure.FileSystem;
using Microsoft.EntityFrameworkCore;

namespace DesignAid.Commands.Part;

/// <summary>
/// パーツを追加するコマンド。
/// </summary>
public class PartAddCommand : Command
{
    public PartAddCommand() : base("add", "パーツを追加")
    {
        this.Add(new Argument<string>("part-number", "型式（例: SP-2026-PLATE-01）"));
        this.Add(new Option<string>("--name", "パーツ名") { IsRequired = true });
        this.Add(new Option<string>("--type", () => "Fabricated", "種別 (Fabricated/Purchased/Standard)"));
        this.Add(new Option<string?>("--material", "材質"));
        this.Add(new Option<decimal?>("--price", "単価（Purchased のみ）"));
        this.Add(new Option<string?>("--currency", "通貨コード（Purchased のみ、デフォルト: JPY）"));
        this.Add(new Option<bool>("--no-git", "Git リポジトリを初期化しない"));

        this.Handler = CommandHandler.Create<string, string, string, string?, decimal?, string?, bool>(ExecuteAsync);
    }

    private static async Task ExecuteAsync(string partNumber, string name, string type, string? material, decimal? price, string? currency, bool noGit)
    {
        if (!Enum.TryParse<PartType>(type, ignoreCase: true, out var partType))
        {
            Console.Error.WriteLine($"[ERROR] 不明なパーツ種別: {type}");
            Console.Error.WriteLine("有効な値: Fabricated, Purchased, Standard");
            Environment.ExitCode = 2;
            return;
        }

        // --price / --currency は Purchased のみ有効
        if ((price != null || currency != null) && partType != PartType.Purchased)
        {
            Console.Error.WriteLine("[ERROR] --price / --currency は Purchased タイプのパーツにのみ指定できます");
            Environment.ExitCode = 2;
            return;
        }

        if (CommandHelper.EnsureDataDirectory() == null) return;
        var componentsDir = CommandHelper.GetComponentsDirectory();
        Directory.CreateDirectory(componentsDir);
        var partPath = Path.Combine(componentsDir, partNumber);

        var partJsonReader = new PartJsonReader();
        if (Directory.Exists(partPath) && partJsonReader.Exists(partPath))
        {
            Console.Error.WriteLine($"[ERROR] パーツは既に存在します: {partNumber}");
            Environment.ExitCode = 1;
            return;
        }

        Directory.CreateDirectory(partPath);
        var partId = Guid.NewGuid();

        // part.json を作成（価格情報があれば含む）
        var partJson = new PartJson
        {
            Id = partId,
            PartNumber = partNumber,
            Name = name,
            Type = partType.ToString(),
            Version = "1.0.0",
            UnitPrice = price,
            Currency = price != null ? (currency ?? "JPY") : currency
        };
        await partJsonReader.WriteAsync(partPath, partJson);

        // DB にも登録
        try
        {
            using var db = CommandHelper.CreateDbContext();
            var existing = await db.Parts.FirstOrDefaultAsync(p => p.PartNumber == new PartNumber(partNumber));
            if (existing == null)
            {
                DesignComponent dbPart = partType switch
                {
                    PartType.Fabricated => FabricatedPart.Create(new PartNumber(partNumber), name, partPath),
                    PartType.Purchased => PurchasedPart.Create(new PartNumber(partNumber), name, partPath),
                    PartType.Standard => StandardPart.Create(new PartNumber(partNumber), name, partPath),
                    _ => FabricatedPart.Create(new PartNumber(partNumber), name, partPath)
                };

                // 購入品の場合、価格情報を設定
                if (dbPart is PurchasedPart pp && price != null)
                {
                    pp.UpdatePurchaseInfo(unitPrice: price, currency: currency ?? "JPY");
                }

                db.Parts.Add(dbPart);
                await db.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[WARN] DB 登録に失敗しました（JSON は作成済み）: {ex.Message}");
        }

        // Git リポジトリを初期化（デフォルト）
        var gitInitialized = false;
        if (!noGit)
        {
            gitInitialized = await InitializeGitRepositoryAsync(partPath);
        }

        Console.WriteLine();
        Console.WriteLine($"Part created: {partNumber}");
        Console.WriteLine($"  Name: {name}");
        Console.WriteLine($"  Type: {partType}");
        Console.WriteLine($"  Path: {partPath}");
        Console.WriteLine($"  ID: {partId}");
        if (price != null)
        {
            Console.WriteLine($"  Price: {price} {currency ?? "JPY"}");
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
