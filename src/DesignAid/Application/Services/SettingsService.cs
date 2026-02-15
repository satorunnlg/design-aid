using System.Text.Json;
using DesignAid.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DesignAid.Application.Services;

/// <summary>
/// DB の Settings テーブルを介した設定管理サービス。
/// シングルトンライクに動作し、全設定を一元管理する。
/// </summary>
public class SettingsService : ISettingsService
{
    /// <summary>
    /// 既知の設定キーとデフォルト値の定義。
    /// </summary>
    public static readonly Dictionary<string, string?> Defaults = new()
    {
        ["database.path"] = "design_aid.db",
        ["vector_search.enabled"] = "true",
        ["vector_search.hnsw_index_path"] = "hnsw_index.bin",
        ["embedding.provider"] = "Mock",
        ["embedding.dimensions"] = "384",
        ["embedding.model"] = null,
        ["embedding.api_key"] = null,
        ["embedding.endpoint"] = null,
        ["hashing.algorithm"] = "SHA256",
        ["backup.s3_bucket"] = "",
        ["backup.s3_prefix"] = "design-aid-backup/",
        ["backup.aws_profile"] = "default",
    };

    private readonly Dictionary<string, string> _cache = new();
    private bool _loaded;

    /// <summary>
    /// DB から全設定をロードする。
    /// </summary>
    public async Task LoadAsync(DesignAidDbContext context)
    {
        var entries = await context.Settings.ToListAsync();
        _cache.Clear();
        foreach (var entry in entries)
        {
            _cache[entry.Key] = entry.Value;
        }
        _loaded = true;
    }

    /// <summary>
    /// 文字列値を取得する。DB 未ロードの場合はデフォルト値を返す。
    /// </summary>
    public string? Get(string key, string? defaultValue = null)
    {
        if (_cache.TryGetValue(key, out var value))
            return value;

        // デフォルト値テーブルから取得
        if (Defaults.TryGetValue(key, out var defVal))
            return defVal ?? defaultValue;

        return defaultValue;
    }

    /// <summary>
    /// bool 値を取得する。
    /// </summary>
    public bool GetBool(string key, bool defaultValue = false)
    {
        var value = Get(key);
        if (value == null) return defaultValue;
        return value.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// int 値を取得する。
    /// </summary>
    public int GetInt(string key, int defaultValue = 0)
    {
        var value = Get(key);
        if (value == null) return defaultValue;
        return int.TryParse(value, out var result) ? result : defaultValue;
    }

    /// <summary>
    /// 設定値を DB に保存する。
    /// </summary>
    public async Task SetAsync(DesignAidDbContext context, string key, string value)
    {
        var entry = await context.Settings.FindAsync(key);
        if (entry != null)
        {
            entry.Value = value;
            entry.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            entry = new SettingsEntry
            {
                Key = key,
                Value = value,
                UpdatedAt = DateTime.UtcNow
            };
            context.Settings.Add(entry);
        }

        await context.SaveChangesAsync();
        _cache[key] = value;
    }

    /// <summary>
    /// デフォルト値を DB に書き込む（存在しないキーのみ）。
    /// </summary>
    public async Task SetDefaultsAsync(DesignAidDbContext context)
    {
        var existingKeys = await context.Settings.Select(s => s.Key).ToListAsync();
        var existingSet = new HashSet<string>(existingKeys);

        foreach (var (key, defaultValue) in Defaults)
        {
            if (!existingSet.Contains(key) && defaultValue != null)
            {
                context.Settings.Add(new SettingsEntry
                {
                    Key = key,
                    Value = defaultValue,
                    UpdatedAt = DateTime.UtcNow
                });
            }
        }

        await context.SaveChangesAsync();

        // キャッシュを更新
        await LoadAsync(context);
    }

    /// <summary>
    /// 全設定を Dictionary で返す（デフォルト値を含む）。
    /// </summary>
    public Dictionary<string, string?> GetAll()
    {
        var result = new Dictionary<string, string?>();

        // デフォルト値を先にセット
        foreach (var (key, defaultValue) in Defaults)
        {
            result[key] = defaultValue;
        }

        // DB の値で上書き
        foreach (var (key, value) in _cache)
        {
            result[key] = value;
        }

        return result;
    }

    /// <summary>
    /// 全設定を非同期で取得する。
    /// </summary>
    public async Task<Dictionary<string, string?>> GetAllAsync(DesignAidDbContext context)
    {
        if (!_loaded)
        {
            await LoadAsync(context);
        }
        return GetAll();
    }

    /// <summary>
    /// 指定キーが既知の設定キーかどうかを判定する。
    /// </summary>
    public static bool IsKnownKey(string key)
    {
        return Defaults.ContainsKey(key);
    }

    /// <summary>
    /// 既知の全キーを返す。
    /// </summary>
    public static IEnumerable<string> GetKnownKeys()
    {
        return Defaults.Keys;
    }

    /// <summary>
    /// config.json から DB に設定を移行する。
    /// 既に DB に存在するキーは上書きしない。
    /// </summary>
    public async Task MigrateFromConfigJsonAsync(DesignAidDbContext context, string configJsonPath)
    {
        if (!File.Exists(configJsonPath))
            return;

        try
        {
            var json = await File.ReadAllTextAsync(configJsonPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var existingKeys = await context.Settings.Select(s => s.Key).ToHashSetAsync();
            var migrated = new List<string>();

            // database セクション
            if (root.TryGetProperty("database", out var db))
            {
                MigrateKey(context, existingKeys, migrated, "database.path",
                    db, "path");
            }

            // vector_search セクション
            if (root.TryGetProperty("vector_search", out var vs))
            {
                MigrateKey(context, existingKeys, migrated, "vector_search.enabled",
                    vs, "enabled");
                MigrateKey(context, existingKeys, migrated, "vector_search.hnsw_index_path",
                    vs, "hnsw_index_path");
            }

            // embedding セクション
            if (root.TryGetProperty("embedding", out var emb))
            {
                MigrateKey(context, existingKeys, migrated, "embedding.provider",
                    emb, "provider");
                MigrateKey(context, existingKeys, migrated, "embedding.dimensions",
                    emb, "dimensions");
                MigrateKey(context, existingKeys, migrated, "embedding.api_key",
                    emb, "api_key");
                MigrateKey(context, existingKeys, migrated, "embedding.endpoint",
                    emb, "endpoint");
                MigrateKey(context, existingKeys, migrated, "embedding.model",
                    emb, "model");
            }

            // backup セクション
            if (root.TryGetProperty("backup", out var bk))
            {
                MigrateKey(context, existingKeys, migrated, "backup.s3_bucket",
                    bk, "s3_bucket");
                MigrateKey(context, existingKeys, migrated, "backup.s3_prefix",
                    bk, "s3_prefix");
                MigrateKey(context, existingKeys, migrated, "backup.aws_profile",
                    bk, "aws_profile");
            }

            if (migrated.Count > 0)
            {
                await context.SaveChangesAsync();
                await LoadAsync(context);
            }
        }
        catch
        {
            // config.json の読み込み失敗時は無視
        }
    }

    private static void MigrateKey(
        DesignAidDbContext context,
        HashSet<string> existingKeys,
        List<string> migrated,
        string settingsKey,
        JsonElement section,
        string jsonProperty)
    {
        if (existingKeys.Contains(settingsKey))
            return;

        if (!section.TryGetProperty(jsonProperty, out var prop))
            return;

        var value = prop.ValueKind switch
        {
            JsonValueKind.String => prop.GetString(),
            JsonValueKind.Number => prop.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };

        if (value == null)
            return;

        context.Settings.Add(new SettingsEntry
        {
            Key = settingsKey,
            Value = value,
            UpdatedAt = DateTime.UtcNow
        });
        existingKeys.Add(settingsKey);
        migrated.Add(settingsKey);
    }
}
