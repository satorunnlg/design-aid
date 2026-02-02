using System.Text.Json;
using System.Text.Json.Serialization;

namespace DesignAid.Infrastructure.FileSystem;

/// <summary>
/// .da-project マーカーファイルのデータ構造。
/// </summary>
public record ProjectMarker
{
    /// <summary>プロジェクトID（UUID）</summary>
    [JsonPropertyName("project_id")]
    public Guid ProjectId { get; init; }

    /// <summary>プロジェクト名</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>登録日時</summary>
    [JsonPropertyName("registered_at")]
    public DateTime RegisteredAt { get; init; }
}

/// <summary>
/// .da-project マーカーファイルの読み書きを行うサービス。
/// </summary>
public class ProjectMarkerService
{
    private const string MarkerFileName = ".da-project";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// マーカーファイルが存在するかどうかを確認する。
    /// </summary>
    /// <param name="directoryPath">プロジェクトディレクトリパス</param>
    /// <returns>存在する場合は true</returns>
    public bool Exists(string directoryPath)
    {
        var markerPath = GetMarkerPath(directoryPath);
        return File.Exists(markerPath);
    }

    /// <summary>
    /// マーカーファイルを読み込む。
    /// </summary>
    /// <param name="directoryPath">プロジェクトディレクトリパス</param>
    /// <returns>ProjectMarker。存在しない場合は null</returns>
    public async Task<ProjectMarker?> ReadAsync(string directoryPath, CancellationToken ct = default)
    {
        var markerPath = GetMarkerPath(directoryPath);

        if (!File.Exists(markerPath))
            return null;

        try
        {
            var json = await File.ReadAllTextAsync(markerPath, ct);
            return JsonSerializer.Deserialize<ProjectMarker>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// マーカーファイルを読み込む（同期版）。
    /// </summary>
    public ProjectMarker? Read(string directoryPath)
    {
        var markerPath = GetMarkerPath(directoryPath);

        if (!File.Exists(markerPath))
            return null;

        try
        {
            var json = File.ReadAllText(markerPath);
            return JsonSerializer.Deserialize<ProjectMarker>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// マーカーファイルを書き込む。
    /// </summary>
    /// <param name="directoryPath">プロジェクトディレクトリパス</param>
    /// <param name="marker">書き込むマーカーデータ</param>
    public async Task WriteAsync(string directoryPath, ProjectMarker marker, CancellationToken ct = default)
    {
        var markerPath = GetMarkerPath(directoryPath);

        // ディレクトリが存在しない場合は作成
        var directory = Path.GetDirectoryName(markerPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(marker, JsonOptions);
        await File.WriteAllTextAsync(markerPath, json, ct);
    }

    /// <summary>
    /// マーカーファイルを書き込む（同期版）。
    /// </summary>
    public void Write(string directoryPath, ProjectMarker marker)
    {
        var markerPath = GetMarkerPath(directoryPath);

        // ディレクトリが存在しない場合は作成
        var directory = Path.GetDirectoryName(markerPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(marker, JsonOptions);
        File.WriteAllText(markerPath, json);
    }

    /// <summary>
    /// 新しいマーカーを作成して書き込む。
    /// </summary>
    /// <param name="directoryPath">プロジェクトディレクトリパス</param>
    /// <param name="projectId">プロジェクトID</param>
    /// <param name="name">プロジェクト名</param>
    /// <returns>作成されたマーカー</returns>
    public async Task<ProjectMarker> CreateAsync(
        string directoryPath,
        Guid projectId,
        string name,
        CancellationToken ct = default)
    {
        var marker = new ProjectMarker
        {
            ProjectId = projectId,
            Name = name,
            RegisteredAt = DateTime.UtcNow
        };

        await WriteAsync(directoryPath, marker, ct);
        return marker;
    }

    /// <summary>
    /// マーカーファイルを削除する。
    /// </summary>
    /// <param name="directoryPath">プロジェクトディレクトリパス</param>
    /// <returns>削除できた場合は true</returns>
    public bool Delete(string directoryPath)
    {
        var markerPath = GetMarkerPath(directoryPath);

        if (!File.Exists(markerPath))
            return false;

        File.Delete(markerPath);
        return true;
    }

    /// <summary>
    /// マーカーファイルのパスを取得する。
    /// </summary>
    private static string GetMarkerPath(string directoryPath)
        => Path.Combine(directoryPath, MarkerFileName);
}
