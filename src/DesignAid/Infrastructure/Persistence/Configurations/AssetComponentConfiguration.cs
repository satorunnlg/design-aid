using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using DesignAid.Domain.Entities;

namespace DesignAid.Infrastructure.Persistence.Configurations;

/// <summary>
/// AssetComponent エンティティの EF Core 設定（中間テーブル）。
/// </summary>
public class AssetComponentConfiguration : IEntityTypeConfiguration<AssetComponent>
{
    public void Configure(EntityTypeBuilder<AssetComponent> builder)
    {
        builder.ToTable("AssetComponents");

        // 複合主キー
        builder.HasKey(ac => new { ac.AssetId, ac.PartId });

        // AssetId（外部キー）
        builder.Property(ac => ac.AssetId)
            .IsRequired()
            .HasConversion(
                v => v.ToString(),
                v => Guid.Parse(v))
            .HasColumnType("TEXT");
        builder.HasIndex(ac => ac.AssetId);

        // PartId（外部キー）
        builder.Property(ac => ac.PartId)
            .IsRequired()
            .HasConversion(
                v => v.ToString(),
                v => Guid.Parse(v))
            .HasColumnType("TEXT");
        builder.HasIndex(ac => ac.PartId);

        // Quantity
        builder.Property(ac => ac.Quantity)
            .IsRequired()
            .HasDefaultValue(1);

        // Notes
        builder.Property(ac => ac.Notes)
            .HasMaxLength(2000);

        // CreatedAt
        builder.Property(ac => ac.CreatedAt)
            .IsRequired()
            .HasConversion(
                v => v.ToString("o"),
                v => DateTime.Parse(v));

        // Asset とのリレーション
        builder.HasOne(ac => ac.Asset)
            .WithMany(a => a.AssetComponents)
            .HasForeignKey(ac => ac.AssetId)
            .OnDelete(DeleteBehavior.Cascade);

        // Part とのリレーション
        builder.HasOne(ac => ac.Part)
            .WithMany(p => p.AssetComponents)
            .HasForeignKey(ac => ac.PartId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
