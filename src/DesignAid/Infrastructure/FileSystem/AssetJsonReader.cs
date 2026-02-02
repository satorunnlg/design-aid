using System.Text.Json;
using System.Text.Json.Serialization;

namespace DesignAid.Infrastructure.FileSystem;

/// <summary>
/// asset.json ファイルのデータ構造。
/// </summary>
public record AssetJson
{
    /// <summary>装置ID（UUID）</summary>
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    /// <summary>装置名</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>表示名</summary>
    [JsonPropertyName("display_name")]
    public string? DisplayName { get; init; }

    /// <summary>説明</summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>作成日時</summary>
    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; init; }
}

/// <summary>
/// asset.json ファイルの読み書きを行うサービス。
/// </summary>
public class AssetJsonReader
{
    private const string AssetFileName = "asset.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// asset.json が存在するかどうかを確認する。
    /// </summary>
    /// <param name="assetDirectoryPath">装置ディレクトリパス</param>
    /// <returns>存在する場合は true</returns>
    public bool Exists(string assetDirectoryPath)
    {
        var assetJsonPath = GetAssetJsonPath(assetDirectoryPath);
        return File.Exists(assetJsonPath);
    }

    /// <summary>
    /// asset.json を読み込む。
    /// </summary>
    /// <param name="assetDirectoryPath">装置ディレクトリパス</param>
    /// <returns>AssetJson。存在しない場合は null</returns>
    public async Task<AssetJson?> ReadAsync(string assetDirectoryPath, CancellationToken ct = default)
    {
        var assetJsonPath = GetAssetJsonPath(assetDirectoryPath);

        if (!File.Exists(assetJsonPath))
            return null;

        try
        {
            var json = await File.ReadAllTextAsync(assetJsonPath, ct);
            return JsonSerializer.Deserialize<AssetJson>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// asset.json を読み込む（同期版）。
    /// </summary>
    public AssetJson? Read(string assetDirectoryPath)
    {
        var assetJsonPath = GetAssetJsonPath(assetDirectoryPath);

        if (!File.Exists(assetJsonPath))
            return null;

        try
        {
            var json = File.ReadAllText(assetJsonPath);
            return JsonSerializer.Deserialize<AssetJson>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// asset.json を書き込む。
    /// </summary>
    /// <param name="assetDirectoryPath">装置ディレクトリパス</param>
    /// <param name="assetJson">書き込むデータ</param>
    public async Task WriteAsync(string assetDirectoryPath, AssetJson assetJson, CancellationToken ct = default)
    {
        var assetJsonPath = GetAssetJsonPath(assetDirectoryPath);

        // ディレクトリが存在しない場合は作成
        if (!Directory.Exists(assetDirectoryPath))
        {
            Directory.CreateDirectory(assetDirectoryPath);
        }

        var json = JsonSerializer.Serialize(assetJson, JsonOptions);
        await File.WriteAllTextAsync(assetJsonPath, json, ct);
    }

    /// <summary>
    /// asset.json を書き込む（同期版）。
    /// </summary>
    public void Write(string assetDirectoryPath, AssetJson assetJson)
    {
        var assetJsonPath = GetAssetJsonPath(assetDirectoryPath);

        // ディレクトリが存在しない場合は作成
        if (!Directory.Exists(assetDirectoryPath))
        {
            Directory.CreateDirectory(assetDirectoryPath);
        }

        var json = JsonSerializer.Serialize(assetJson, JsonOptions);
        File.WriteAllText(assetJsonPath, json);
    }

    /// <summary>
    /// 新しい asset.json を作成して書き込む。
    /// </summary>
    /// <param name="assetDirectoryPath">装置ディレクトリパス</param>
    /// <param name="assetId">装置ID</param>
    /// <param name="name">装置名</param>
    /// <param name="displayName">表示名</param>
    /// <param name="description">説明</param>
    /// <returns>作成された AssetJson</returns>
    public async Task<AssetJson> CreateAsync(
        string assetDirectoryPath,
        Guid assetId,
        string name,
        string? displayName = null,
        string? description = null,
        CancellationToken ct = default)
    {
        var assetJson = new AssetJson
        {
            Id = assetId,
            Name = name,
            DisplayName = displayName,
            Description = description,
            CreatedAt = DateTime.UtcNow
        };

        await WriteAsync(assetDirectoryPath, assetJson, ct);

        // components ディレクトリも作成
        var componentsPath = Path.Combine(assetDirectoryPath, "components");
        if (!Directory.Exists(componentsPath))
        {
            Directory.CreateDirectory(componentsPath);
        }

        return assetJson;
    }

    /// <summary>
    /// asset.json を削除する。
    /// </summary>
    /// <param name="assetDirectoryPath">装置ディレクトリパス</param>
    /// <returns>削除できた場合は true</returns>
    public bool Delete(string assetDirectoryPath)
    {
        var assetJsonPath = GetAssetJsonPath(assetDirectoryPath);

        if (!File.Exists(assetJsonPath))
            return false;

        File.Delete(assetJsonPath);
        return true;
    }

    /// <summary>
    /// asset.json のパスを取得する。
    /// </summary>
    private static string GetAssetJsonPath(string assetDirectoryPath)
        => Path.Combine(assetDirectoryPath, AssetFileName);
}
