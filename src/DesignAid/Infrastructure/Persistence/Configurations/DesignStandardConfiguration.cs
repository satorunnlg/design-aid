using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using DesignAid.Domain.Entities;

namespace DesignAid.Infrastructure.Persistence.Configurations;

/// <summary>
/// DesignStandard エンティティの EF Core 設定。
/// </summary>
public class DesignStandardConfiguration : IEntityTypeConfiguration<DesignStandard>
{
    public void Configure(EntityTypeBuilder<DesignStandard> builder)
    {
        builder.ToTable("Standards");

        // 主キー（StandardId）
        builder.HasKey(s => s.StandardId);
        builder.Property(s => s.StandardId)
            .HasMaxLength(100);

        // Name
        builder.Property(s => s.Name)
            .IsRequired()
            .HasMaxLength(255);

        // Description
        builder.Property(s => s.Description)
            .HasMaxLength(2000);

        // ValidationRuleJson（JSON として保存）
        builder.Property(s => s.ValidationRuleJson)
            .HasColumnType("TEXT");
    }
}
