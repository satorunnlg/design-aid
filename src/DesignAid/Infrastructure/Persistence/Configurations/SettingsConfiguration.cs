using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DesignAid.Infrastructure.Persistence.Configurations;

/// <summary>
/// SettingsEntry エンティティの EF Core 設定。
/// </summary>
public class SettingsConfiguration : IEntityTypeConfiguration<SettingsEntry>
{
    public void Configure(EntityTypeBuilder<SettingsEntry> builder)
    {
        builder.ToTable("Settings");

        // 主キー: Key（dot-notation 文字列）
        builder.HasKey(s => s.Key);
        builder.Property(s => s.Key)
            .IsRequired()
            .HasColumnType("TEXT");

        // Value
        builder.Property(s => s.Value)
            .IsRequired()
            .HasColumnType("TEXT");

        // UpdatedAt（ISO 8601 変換）
        builder.Property(s => s.UpdatedAt)
            .IsRequired()
            .HasConversion(
                v => v.ToString("o"),
                v => DateTime.Parse(v));
    }
}
