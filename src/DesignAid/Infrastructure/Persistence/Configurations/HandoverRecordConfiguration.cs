using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using DesignAid.Domain.Entities;
using DesignAid.Domain.ValueObjects;

namespace DesignAid.Infrastructure.Persistence.Configurations;

/// <summary>
/// HandoverRecord エンティティの EF Core 設定。
/// </summary>
public class HandoverRecordConfiguration : IEntityTypeConfiguration<HandoverRecord>
{
    public void Configure(EntityTypeBuilder<HandoverRecord> builder)
    {
        builder.ToTable("HandoverHistory");

        // 主キー（自動採番）
        builder.HasKey(h => h.Id);
        builder.Property(h => h.Id)
            .ValueGeneratedOnAdd();

        // PartId（外部キー）
        builder.Property(h => h.PartId)
            .IsRequired()
            .HasConversion(
                v => v.ToString(),
                v => Guid.Parse(v))
            .HasColumnType("TEXT");
        builder.HasIndex(h => h.PartId);

        // CommittedHash
        builder.Property(h => h.CommittedHash)
            .IsRequired()
            .HasMaxLength(128);

        // Status（HandoverStatus 列挙型を TEXT として保存）
        builder.Property(h => h.Status)
            .IsRequired()
            .HasConversion(
                v => v.ToString(),
                v => Enum.Parse<HandoverStatus>(v));
        builder.HasIndex(h => h.Status);

        // OrderDate
        builder.Property(h => h.OrderDate)
            .IsRequired()
            .HasConversion(
                v => v.ToString("o"),
                v => DateTime.Parse(v));

        // DeliveryDate（nullable）
        builder.Property(h => h.DeliveryDate)
            .HasConversion(
                v => v == null ? null : v.Value.ToString("o"),
                v => v == null ? null : DateTime.Parse(v));

        // Notes
        builder.Property(h => h.Notes)
            .HasMaxLength(4000);

        // Part とのリレーション
        builder.HasOne(h => h.Part)
            .WithMany()
            .HasForeignKey(h => h.PartId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
