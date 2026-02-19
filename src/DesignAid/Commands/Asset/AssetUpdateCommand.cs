using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
using DesignAid.Infrastructure.FileSystem;

namespace DesignAid.Commands.Asset;

/// <summary>
/// 装置情報を更新するコマンド。
/// asset.json のフィールドを上書きする。
/// </summary>
public class AssetUpdateCommand : Command
{
    public AssetUpdateCommand() : base("update", "装置情報を更新")
    {
        this.Add(new Argument<string>("name", "更新する装置名"));
        this.Add(new Option<string?>("--display-name", "表示名"));
        this.Add(new Option<string?>("--description", "説明"));
        this.Add(new Option<string?>("--status", "ステータス (active/dormant/archived/third_party)"));
        this.Add(new Option<string?>("--tags", "タグ一括設定（カンマ区切り）"));
        this.Add(new Option<string?>("--add-tag", "タグを追加"));
        this.Add(new Option<string?>("--remove-tag", "タグを削除"));

        this.Handler = CommandHandler.Create<string, string?, string?, string?, string?, string?, string?>(ExecuteAsync);
    }

    private static async Task ExecuteAsync(
        string name, string? displayName, string? description,
        string? status, string? tags, string? addTag, string? removeTag)
    {
        if (displayName == null && description == null && status == null
            && tags == null && addTag == null && removeTag == null)
        {
            Console.Error.WriteLine("[ERROR] 更新するフィールドを1つ以上指定してください（--display-name / --description / --status / --tags / --add-tag / --remove-tag）");
            Environment.ExitCode = 2;
            return;
        }

        // ステータス値のバリデーション
        if (status != null)
        {
            var validStatuses = new[] { "active", "dormant", "archived", "third_party" };
            if (!validStatuses.Contains(status.ToLowerInvariant()))
            {
                Console.Error.WriteLine($"[ERROR] 無効なステータスです: {status}");
                Console.Error.WriteLine($"  有効な値: {string.Join(", ", validStatuses)}");
                Environment.ExitCode = 2;
                return;
            }
        }

        if (CommandHelper.EnsureDataDirectory() == null) return;
        var assetsDir = CommandHelper.GetAssetsDirectory();
        var assetPath = Path.Combine(assetsDir, name);

        var assetJsonReader = new AssetJsonReader();
        if (!Directory.Exists(assetPath) || !assetJsonReader.Exists(assetPath))
        {
            Console.Error.WriteLine($"[ERROR] 装置が見つかりません: {name}");
            Environment.ExitCode = 1;
            return;
        }

        var assetJson = await assetJsonReader.ReadAsync(assetPath);
        if (assetJson == null)
        {
            Console.Error.WriteLine($"[ERROR] asset.json の読み込みに失敗しました: {name}");
            Environment.ExitCode = 1;
            return;
        }

        // タグの計算
        List<string>? newTags = null;
        if (tags != null)
        {
            // 一括設定
            newTags = tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        }
        else if (addTag != null || removeTag != null)
        {
            // 既存タグに対して追加/削除
            newTags = (assetJson.Tags ?? new List<string>()).ToList();
            if (addTag != null && !newTags.Contains(addTag, StringComparer.OrdinalIgnoreCase))
            {
                newTags.Add(addTag);
            }
            if (removeTag != null)
            {
                newTags.RemoveAll(t => t.Equals(removeTag, StringComparison.OrdinalIgnoreCase));
            }
        }

        // 指定されたフィールドのみ上書き
        var updated = assetJson with
        {
            DisplayName = displayName ?? assetJson.DisplayName,
            Description = description ?? assetJson.Description,
            Status = status?.ToLowerInvariant() ?? assetJson.Status,
            Tags = newTags ?? assetJson.Tags
        };

        await assetJsonReader.WriteAsync(assetPath, updated);

        Console.WriteLine($"Asset updated: {name}");
        if (displayName != null) Console.WriteLine($"  DisplayName: {displayName}");
        if (description != null) Console.WriteLine($"  Description: {description}");
        if (status != null) Console.WriteLine($"  Status: {status.ToLowerInvariant()}");
        if (tags != null || addTag != null || removeTag != null)
        {
            var finalTags = updated.Tags ?? new List<string>();
            Console.WriteLine($"  Tags: {(finalTags.Count > 0 ? string.Join(", ", finalTags) : "(none)")}");
        }
    }
}
