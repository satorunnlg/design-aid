using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using DesignAid.Domain.Entities;
using DesignAid.Domain.ValueObjects;

namespace DesignAid.Infrastructure.Persistence.Configurations;

/// <summary>
/// DesignComponent エンティティの EF Core 設定（TPH パターン）。
/// FabricatedPart, PurchasedPart, StandardPart を単一テーブルで管理。
/// </summary>
public class DesignComponentConfiguration : IEntityTypeConfiguration<DesignComponent>
{
    public void Configure(EntityTypeBuilder<DesignComponent> builder)
    {
        builder.ToTable("Parts");

        // 主キー（UUID を TEXT として保存）
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id)
            .HasConversion(
                v => v.ToString(),
                v => Guid.Parse(v))
            .HasColumnType("TEXT");

        // Type プロパティを無視（抽象プロパティのため）
        builder.Ignore(c => c.Type);

        // TPH（Table-Per-Hierarchy）設定 - PartTypeDiscriminator カラムでサブクラスを判別
        builder.HasDiscriminator<string>("PartTypeDiscriminator")
            .HasValue<FabricatedPart>("Fabricated")
            .HasValue<PurchasedPart>("Purchased")
            .HasValue<StandardPart>("Standard");

        // PartNumber（値オブジェクト、グローバルユニーク）
        builder.Property(c => c.PartNumber)
            .IsRequired()
            .HasMaxLength(255)
            .HasConversion(
                v => v.Value,
                v => new PartNumber(v));
        builder.HasIndex(c => c.PartNumber)
            .IsUnique();

        // Name
        builder.Property(c => c.Name)
            .IsRequired()
            .HasMaxLength(255);

        // Version
        builder.Property(c => c.Version)
            .IsRequired()
            .HasMaxLength(50);

        // CurrentHash
        builder.Property(c => c.CurrentHash)
            .IsRequired()
            .HasMaxLength(128);

        // DirectoryPath
        builder.Property(c => c.DirectoryPath)
            .IsRequired()
            .HasMaxLength(1024);

        // ArtifactHashes（JSON として保存）
        builder.Property(c => c.ArtifactHashes)
            .HasColumnName("ArtifactHashesJson")
            .HasConversion(
                v => JsonSerializer.Serialize(
                    v.ToDictionary(kv => kv.Key, kv => kv.Value.Value),
                    (JsonSerializerOptions?)null),
                v => DeserializeArtifactHashes(v));

        // Metadata（JSON として保存）
        builder.Property(c => c.Metadata)
            .HasColumnName("MetaDataJson")
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => DeserializeMetadata(v));

        // StandardIds（JSON として保存）
        builder.Property(c => c.StandardIds)
            .HasColumnName("StandardIdsJson")
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => DeserializeStringList(v));

        // Status（HandoverStatus 列挙型を TEXT として保存）
        builder.Property(c => c.Status)
            .IsRequired()
            .HasConversion(
                v => v.ToString(),
                v => Enum.Parse<HandoverStatus>(v));

        // Memo
        builder.Property(c => c.Memo)
            .HasMaxLength(4000);

        // 日時
        builder.Property(c => c.CreatedAt)
            .IsRequired()
            .HasConversion(
                v => v.ToString("o"),
                v => DateTime.Parse(v));

        builder.Property(c => c.UpdatedAt)
            .IsRequired()
            .HasConversion(
                v => v.ToString("o"),
                v => DateTime.Parse(v));

    }

    /// <summary>
    /// ArtifactHashes の JSON デシリアライズ。
    /// </summary>
    private static Dictionary<string, FileHash> DeserializeArtifactHashes(string json)
    {
        if (string.IsNullOrEmpty(json))
            return new Dictionary<string, FileHash>();

        var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json)
            ?? new Dictionary<string, string>();

        return dict.ToDictionary(kv => kv.Key, kv => new FileHash(kv.Value));
    }

    /// <summary>
    /// Metadata の JSON デシリアライズ。
    /// </summary>
    private static Dictionary<string, object> DeserializeMetadata(string json)
    {
        if (string.IsNullOrEmpty(json))
            return new Dictionary<string, object>();

        return JsonSerializer.Deserialize<Dictionary<string, object>>(json)
            ?? new Dictionary<string, object>();
    }

    /// <summary>
    /// StandardIds の JSON デシリアライズ。
    /// </summary>
    private static List<string> DeserializeStringList(string json)
    {
        if (string.IsNullOrEmpty(json))
            return new List<string>();

        return JsonSerializer.Deserialize<List<string>>(json)
            ?? new List<string>();
    }
}
