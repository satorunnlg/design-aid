using System.Text.Json;
using System.Text.Json.Serialization;
using DesignAid.Domain.Entities;
using DesignAid.Domain.ValueObjects;

namespace DesignAid.Infrastructure.FileSystem;

/// <summary>
/// part.json の成果物エントリ。
/// </summary>
public record ArtifactEntry
{
    /// <summary>ファイルの相対パス</summary>
    [JsonPropertyName("path")]
    public string Path { get; init; } = string.Empty;

    /// <summary>ファイルハッシュ</summary>
    [JsonPropertyName("hash")]
    public string Hash { get; init; } = string.Empty;
}

/// <summary>
/// part.json ファイルのデータ構造。
/// 装置との関連は DB の中間テーブルで管理するため asset_id は持たない。
/// </summary>
public record PartJson
{
    /// <summary>パーツID（UUID）</summary>
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    /// <summary>型式</summary>
    [JsonPropertyName("part_number")]
    public string PartNumber { get; init; } = string.Empty;

    /// <summary>パーツ名</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>パーツ種別</summary>
    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    /// <summary>バージョン</summary>
    [JsonPropertyName("version")]
    public string Version { get; init; } = "1.0.0";

    /// <summary>成果物リスト</summary>
    [JsonPropertyName("artifacts")]
    public List<ArtifactEntry> Artifacts { get; init; } = new();

    /// <summary>適用設計基準ID</summary>
    [JsonPropertyName("standards")]
    public List<string> Standards { get; init; } = new();

    /// <summary>メタデータ</summary>
    [JsonPropertyName("metadata")]
    public Dictionary<string, object>? Metadata { get; init; }

    /// <summary>メモ</summary>
    [JsonPropertyName("memo")]
    public string? Memo { get; init; }
}

/// <summary>
/// part.json ファイルの読み書きを行うサービス。
/// </summary>
public class PartJsonReader
{
    private const string PartFileName = "part.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// part.json が存在するかどうかを確認する。
    /// </summary>
    /// <param name="partDirectoryPath">パーツディレクトリパス</param>
    /// <returns>存在する場合は true</returns>
    public bool Exists(string partDirectoryPath)
    {
        var partJsonPath = GetPartJsonPath(partDirectoryPath);
        return File.Exists(partJsonPath);
    }

    /// <summary>
    /// part.json を読み込む。
    /// </summary>
    /// <param name="partDirectoryPath">パーツディレクトリパス</param>
    /// <returns>PartJson。存在しない場合は null</returns>
    public async Task<PartJson?> ReadAsync(string partDirectoryPath, CancellationToken ct = default)
    {
        var partJsonPath = GetPartJsonPath(partDirectoryPath);

        if (!File.Exists(partJsonPath))
            return null;

        try
        {
            var json = await File.ReadAllTextAsync(partJsonPath, ct);
            return JsonSerializer.Deserialize<PartJson>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// part.json を読み込む（同期版）。
    /// </summary>
    public PartJson? Read(string partDirectoryPath)
    {
        var partJsonPath = GetPartJsonPath(partDirectoryPath);

        if (!File.Exists(partJsonPath))
            return null;

        try
        {
            var json = File.ReadAllText(partJsonPath);
            return JsonSerializer.Deserialize<PartJson>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// part.json を書き込む。
    /// </summary>
    /// <param name="partDirectoryPath">パーツディレクトリパス</param>
    /// <param name="partJson">書き込むデータ</param>
    public async Task WriteAsync(string partDirectoryPath, PartJson partJson, CancellationToken ct = default)
    {
        var partJsonPath = GetPartJsonPath(partDirectoryPath);

        // ディレクトリが存在しない場合は作成
        if (!Directory.Exists(partDirectoryPath))
        {
            Directory.CreateDirectory(partDirectoryPath);
        }

        var json = JsonSerializer.Serialize(partJson, JsonOptions);
        await File.WriteAllTextAsync(partJsonPath, json, ct);
    }

    /// <summary>
    /// part.json を書き込む（同期版）。
    /// </summary>
    public void Write(string partDirectoryPath, PartJson partJson)
    {
        var partJsonPath = GetPartJsonPath(partDirectoryPath);

        // ディレクトリが存在しない場合は作成
        if (!Directory.Exists(partDirectoryPath))
        {
            Directory.CreateDirectory(partDirectoryPath);
        }

        var json = JsonSerializer.Serialize(partJson, JsonOptions);
        File.WriteAllText(partJsonPath, json);
    }

    /// <summary>
    /// エンティティから PartJson を生成する。
    /// </summary>
    /// <param name="component">パーツエンティティ</param>
    /// <returns>PartJson</returns>
    public PartJson ToPartJson(DesignComponent component)
    {
        var artifacts = component.ArtifactHashes
            .Select(kv => new ArtifactEntry { Path = kv.Key, Hash = kv.Value })
            .ToList();

        return new PartJson
        {
            Id = component.Id,
            PartNumber = component.PartNumber,
            Name = component.Name,
            Type = component.Type.ToString(),
            Version = component.Version,
            Artifacts = artifacts,
            Standards = component.StandardIds,
            Metadata = component.Metadata.Count > 0 ? component.Metadata : null,
            Memo = component.Memo
        };
    }

    /// <summary>
    /// PartJson からパーツ種別を解析する。
    /// </summary>
    /// <param name="partJson">パーツJSON</param>
    /// <returns>パーツ種別</returns>
    public static PartType ParsePartType(PartJson partJson)
    {
        return partJson.Type.ToLowerInvariant() switch
        {
            "fabricated" => PartType.Fabricated,
            "purchased" => PartType.Purchased,
            "standard" => PartType.Standard,
            _ => throw new ArgumentException($"不明なパーツ種別: {partJson.Type}")
        };
    }

    /// <summary>
    /// 新しい part.json を作成して書き込む。
    /// </summary>
    /// <param name="partDirectoryPath">パーツディレクトリパス</param>
    /// <param name="partId">パーツID</param>
    /// <param name="partNumber">型式</param>
    /// <param name="name">パーツ名</param>
    /// <param name="type">パーツ種別</param>
    /// <returns>作成された PartJson</returns>
    public async Task<PartJson> CreateAsync(
        string partDirectoryPath,
        Guid partId,
        string partNumber,
        string name,
        PartType type,
        CancellationToken ct = default)
    {
        var partJson = new PartJson
        {
            Id = partId,
            PartNumber = partNumber,
            Name = name,
            Type = type.ToString(),
            Version = "1.0.0"
        };

        await WriteAsync(partDirectoryPath, partJson, ct);
        return partJson;
    }

    /// <summary>
    /// part.json を削除する。
    /// </summary>
    /// <param name="partDirectoryPath">パーツディレクトリパス</param>
    /// <returns>削除できた場合は true</returns>
    public bool Delete(string partDirectoryPath)
    {
        var partJsonPath = GetPartJsonPath(partDirectoryPath);

        if (!File.Exists(partJsonPath))
            return false;

        File.Delete(partJsonPath);
        return true;
    }

    /// <summary>
    /// part.json のパスを取得する。
    /// </summary>
    private static string GetPartJsonPath(string partDirectoryPath)
        => Path.Combine(partDirectoryPath, PartFileName);
}
