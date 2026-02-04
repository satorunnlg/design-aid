using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using DesignAid.Domain.Entities;

namespace DesignAid.Infrastructure.Persistence.Configurations;

/// <summary>
/// Project エンティティの EF Core 設定。
/// </summary>
public class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.ToTable("Projects");

        // 主キー（UUID を TEXT として保存）
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id)
            .HasConversion(
                v => v.ToString(),
                v => Guid.Parse(v))
            .HasColumnType("TEXT");

        // Name（ユニーク制約）
        builder.Property(p => p.Name)
            .IsRequired()
            .HasMaxLength(255);
        builder.HasIndex(p => p.Name)
            .IsUnique();

        // DisplayName
        builder.Property(p => p.DisplayName)
            .HasMaxLength(255);

        // Description
        builder.Property(p => p.Description)
            .HasMaxLength(2000);

        // DirectoryPath
        builder.Property(p => p.DirectoryPath)
            .IsRequired()
            .HasMaxLength(1024);

        // 日時（ISO8601 TEXT として保存）
        builder.Property(p => p.CreatedAt)
            .IsRequired()
            .HasConversion(
                v => v.ToString("o"),
                v => DateTime.Parse(v));

        builder.Property(p => p.UpdatedAt)
            .IsRequired()
            .HasConversion(
                v => v.ToString("o"),
                v => DateTime.Parse(v));

        // 装置とのリレーション（1対多）
        builder.HasMany(p => p.Assets)
            .WithOne(a => a.Project)
            .HasForeignKey(a => a.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
