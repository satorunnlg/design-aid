using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using DesignAid.Domain.Entities;

namespace DesignAid.Infrastructure.Persistence.Configurations;

/// <summary>
/// Asset エンティティの EF Core 設定。
/// </summary>
public class AssetConfiguration : IEntityTypeConfiguration<Asset>
{
    public void Configure(EntityTypeBuilder<Asset> builder)
    {
        builder.ToTable("Assets");

        // 主キー（UUID を TEXT として保存）
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id)
            .HasConversion(
                v => v.ToString(),
                v => Guid.Parse(v))
            .HasColumnType("TEXT");

        // ProjectId（外部キー）
        builder.Property(a => a.ProjectId)
            .IsRequired()
            .HasConversion(
                v => v.ToString(),
                v => Guid.Parse(v))
            .HasColumnType("TEXT");
        builder.HasIndex(a => a.ProjectId);

        // Name（プロジェクト内でユニーク）
        builder.Property(a => a.Name)
            .IsRequired()
            .HasMaxLength(255);
        builder.HasIndex(a => a.Name);
        builder.HasIndex(a => new { a.ProjectId, a.Name })
            .IsUnique();

        // DisplayName
        builder.Property(a => a.DisplayName)
            .HasMaxLength(255);

        // Description
        builder.Property(a => a.Description)
            .HasMaxLength(2000);

        // DirectoryPath
        builder.Property(a => a.DirectoryPath)
            .IsRequired()
            .HasMaxLength(1024);

        // 日時
        builder.Property(a => a.CreatedAt)
            .IsRequired()
            .HasConversion(
                v => v.ToString("o"),
                v => DateTime.Parse(v));

        builder.Property(a => a.UpdatedAt)
            .IsRequired()
            .HasConversion(
                v => v.ToString("o"),
                v => DateTime.Parse(v));

        // Project とのリレーションは ProjectConfiguration で設定済み
    }
}
