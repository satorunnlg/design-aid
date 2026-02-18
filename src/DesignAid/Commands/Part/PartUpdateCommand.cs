using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
using DesignAid.Domain.Entities;
using DesignAid.Infrastructure.FileSystem;

namespace DesignAid.Commands.Part;

/// <summary>
/// パーツ情報を更新するコマンド。
/// part.json のフィールドを上書きする。
/// </summary>
public class PartUpdateCommand : Command
{
    public PartUpdateCommand() : base("update", "パーツ情報を更新")
    {
        this.Add(new Argument<string>("part-number", "更新する型式"));
        this.Add(new Option<string?>("--name", "パーツ名"));
        this.Add(new Option<string?>("--memo", "メモ"));
        this.Add(new Option<string?>("--material", "材質（metadata に設定）"));
        this.Add(new Option<decimal?>("--price", "単価（Purchased のみ）"));
        this.Add(new Option<string?>("--currency", "通貨コード（Purchased のみ）"));

        this.Handler = CommandHandler.Create<string, string?, string?, string?, decimal?, string?>(ExecuteAsync);
    }

    private static async Task ExecuteAsync(
        string partNumber,
        string? name,
        string? memo,
        string? material,
        decimal? price,
        string? currency)
    {
        if (CommandHelper.EnsureDataDirectory() == null) return;
        var componentsDir = CommandHelper.GetComponentsDirectory();
        var partPath = Path.Combine(componentsDir, partNumber);

        var partJsonReader = new PartJsonReader();
        if (!Directory.Exists(partPath) || !partJsonReader.Exists(partPath))
        {
            Console.Error.WriteLine($"[ERROR] パーツが見つかりません: {partNumber}");
            Environment.ExitCode = 1;
            return;
        }

        var partJson = await partJsonReader.ReadAsync(partPath);
        if (partJson == null)
        {
            Console.Error.WriteLine($"[ERROR] part.json の読み込みに失敗しました: {partNumber}");
            Environment.ExitCode = 1;
            return;
        }

        // --price / --currency は Purchased のみ有効
        var partType = PartJsonReader.ParsePartType(partJson);
        if ((price != null || currency != null) && partType != PartType.Purchased)
        {
            Console.Error.WriteLine("[ERROR] --price / --currency は Purchased タイプのパーツにのみ指定できます");
            Environment.ExitCode = 2;
            return;
        }

        // 更新内容を構築（指定されたフィールドのみ上書き）
        var metadata = partJson.Metadata != null
            ? new Dictionary<string, object>(partJson.Metadata)
            : null;

        if (material != null)
        {
            metadata ??= new Dictionary<string, object>();
            metadata["material"] = material;
        }

        var updated = partJson with
        {
            Name = name ?? partJson.Name,
            Memo = memo ?? partJson.Memo,
            Metadata = metadata,
            UnitPrice = price ?? partJson.UnitPrice,
            Currency = currency ?? partJson.Currency
        };

        await partJsonReader.WriteAsync(partPath, updated);

        // 変更内容を表示
        Console.WriteLine($"Part updated: {partNumber}");
        if (name != null) Console.WriteLine($"  Name: {name}");
        if (memo != null) Console.WriteLine($"  Memo: {memo}");
        if (material != null) Console.WriteLine($"  Material: {material}");
        if (price != null) Console.WriteLine($"  Price: {price} {currency ?? partJson.Currency ?? "JPY"}");
        if (currency != null) Console.WriteLine($"  Currency: {currency}");
    }
}
