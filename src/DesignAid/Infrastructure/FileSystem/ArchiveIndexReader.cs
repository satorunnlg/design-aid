using System.Text.Json;
using System.Text.Json.Serialization;

namespace DesignAid.Infrastructure.FileSystem;

/// <summary>
/// アーカイブインデックス（archive_index.json）の読み書きを担当する。
/// </summary>
public class ArchiveIndexReader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// アーカイブインデックスを読み込む。
    /// </summary>
    public async Task<ArchiveIndex> LoadAsync(string indexPath)
    {
        if (!File.Exists(indexPath))
        {
            return new ArchiveIndex();
        }

        var json = await File.ReadAllTextAsync(indexPath);
        return JsonSerializer.Deserialize<ArchiveIndex>(json, JsonOptions) ?? new ArchiveIndex();
    }

    /// <summary>
    /// アーカイブインデックスを保存する。
    /// </summary>
    public async Task SaveAsync(string indexPath, ArchiveIndex index)
    {
        var json = JsonSerializer.Serialize(index, JsonOptions);
        await File.WriteAllTextAsync(indexPath, json);
    }

    /// <summary>
    /// アーカイブエントリを追加する。
    /// </summary>
    public async Task AddEntryAsync(string indexPath, ArchiveEntry entry)
    {
        var index = await LoadAsync(indexPath);
        index.Archived.Add(entry);
        await SaveAsync(indexPath, index);
    }

    /// <summary>
    /// アーカイブエントリを削除する。
    /// </summary>
    public async Task RemoveEntryAsync(string indexPath, string type, string name)
    {
        var index = await LoadAsync(indexPath);
        index.Archived.RemoveAll(e => e.Type == type && e.Name == name);
        await SaveAsync(indexPath, index);
    }

    /// <summary>
    /// 指定された名前がアーカイブ済みかどうかを確認する。
    /// </summary>
    public async Task<ArchiveEntry?> FindEntryAsync(string indexPath, string type, string name)
    {
        var index = await LoadAsync(indexPath);
        return index.Archived.FirstOrDefault(e => e.Type == type && e.Name == name);
    }
}

/// <summary>
/// アーカイブインデックスのルート。
/// </summary>
public class ArchiveIndex
{
    [JsonPropertyName("archived")]
    public List<ArchiveEntry> Archived { get; set; } = [];
}

/// <summary>
/// アーカイブエントリ。
/// </summary>
public class ArchiveEntry
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("display_name")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("archived_at")]
    public DateTime ArchivedAt { get; set; }

    [JsonPropertyName("original_path")]
    public string OriginalPath { get; set; } = string.Empty;

    [JsonPropertyName("archive_path")]
    public string ArchivePath { get; set; } = string.Empty;

    [JsonPropertyName("original_size_bytes")]
    public long OriginalSizeBytes { get; set; }

    [JsonPropertyName("archive_size_bytes")]
    public long ArchiveSizeBytes { get; set; }

    [JsonPropertyName("vector_ids")]
    public List<string>? VectorIds { get; set; }
}
