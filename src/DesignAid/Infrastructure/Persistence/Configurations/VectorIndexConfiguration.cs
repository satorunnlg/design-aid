using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using DesignAid.Infrastructure.VectorSearch;

namespace DesignAid.Infrastructure.Persistence.Configurations;

/// <summary>
/// VectorIndexEntry エンティティの EF Core 設定。
/// </summary>
public class VectorIndexConfiguration : IEntityTypeConfiguration<VectorIndexEntry>
{
    public void Configure(EntityTypeBuilder<VectorIndexEntry> builder)
    {
        builder.ToTable("VectorIndex");

        // 主キー（自動採番）
        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id)
            .ValueGeneratedOnAdd();

        // PartId
        builder.Property(v => v.PartId)
            .IsRequired()
            .HasColumnType("TEXT");
        builder.HasIndex(v => v.PartId);

        // PartNumber
        builder.Property(v => v.PartNumber)
            .IsRequired()
            .HasColumnType("TEXT");

        // Content
        builder.Property(v => v.Content)
            .IsRequired()
            .HasColumnType("TEXT");

        // Embedding（BLOB）
        builder.Property(v => v.Embedding)
            .IsRequired()
            .HasColumnType("BLOB");

        // Dimensions
        builder.Property(v => v.Dimensions)
            .IsRequired();

        // メタデータフィールド
        builder.Property(v => v.AssetId).HasColumnType("TEXT");
        builder.Property(v => v.AssetName).HasColumnType("TEXT");
        builder.Property(v => v.ProjectId).HasColumnType("TEXT");
        builder.Property(v => v.ProjectName).HasColumnType("TEXT");
        builder.Property(v => v.Type).HasColumnType("TEXT");
        builder.Property(v => v.FilePath).HasColumnType("TEXT");

        // タイムスタンプ
        builder.Property(v => v.CreatedAt)
            .IsRequired()
            .HasConversion(
                v => v.ToString("o"),
                v => DateTime.Parse(v));

        builder.Property(v => v.UpdatedAt)
            .IsRequired()
            .HasConversion(
                v => v.ToString("o"),
                v => DateTime.Parse(v));
    }
}
