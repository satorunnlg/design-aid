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

        // Name（グローバルでユニーク）
        builder.Property(a => a.Name)
            .IsRequired()
            .HasMaxLength(255);
        builder.HasIndex(a => a.Name)
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
    }
}

/// <summary>
/// AssetSubAsset エンティティ（装置-子装置中間テーブル）の EF Core 設定。
/// </summary>
public class AssetSubAssetConfiguration : IEntityTypeConfiguration<AssetSubAsset>
{
    public void Configure(EntityTypeBuilder<AssetSubAsset> builder)
    {
        builder.ToTable("AssetSubAssets");

        // 複合主キー
        builder.HasKey(asa => new { asa.ParentAssetId, asa.ChildAssetId });

        // 外部キー設定
        builder.Property(asa => asa.ParentAssetId)
            .HasConversion(
                v => v.ToString(),
                v => Guid.Parse(v))
            .HasColumnType("TEXT");

        builder.Property(asa => asa.ChildAssetId)
            .HasConversion(
                v => v.ToString(),
                v => Guid.Parse(v))
            .HasColumnType("TEXT");

        // インデックス
        builder.HasIndex(asa => asa.ParentAssetId);
        builder.HasIndex(asa => asa.ChildAssetId);

        // リレーション
        builder.HasOne(asa => asa.ParentAsset)
            .WithMany(a => a.ChildAssets)
            .HasForeignKey(asa => asa.ParentAssetId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(asa => asa.ChildAsset)
            .WithMany(a => a.ParentAssets)
            .HasForeignKey(asa => asa.ChildAssetId)
            .OnDelete(DeleteBehavior.Restrict);

        // Quantity
        builder.Property(asa => asa.Quantity)
            .HasDefaultValue(1);

        // Notes
        builder.Property(asa => asa.Notes)
            .HasMaxLength(2000);

        // CreatedAt
        builder.Property(asa => asa.CreatedAt)
            .IsRequired()
            .HasConversion(
                v => v.ToString("o"),
                v => DateTime.Parse(v));
    }
}
