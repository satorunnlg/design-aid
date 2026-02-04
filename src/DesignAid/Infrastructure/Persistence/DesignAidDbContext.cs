using Microsoft.EntityFrameworkCore;
using DesignAid.Domain.Entities;
using DesignAid.Infrastructure.Persistence.Configurations;

namespace DesignAid.Infrastructure.Persistence;

/// <summary>
/// Design Aid の EF Core DbContext。
/// SQLite を使用し、プロジェクト・装置・部品・手配履歴を管理する。
/// </summary>
public class DesignAidDbContext : DbContext
{
    /// <summary>プロジェクト</summary>
    public DbSet<Project> Projects => Set<Project>();

    /// <summary>装置</summary>
    public DbSet<Asset> Assets => Set<Asset>();

    /// <summary>部品（基底クラス、TPH により FabricatedPart/PurchasedPart/StandardPart を含む）</summary>
    public DbSet<DesignComponent> Parts => Set<DesignComponent>();

    /// <summary>製作物</summary>
    public DbSet<FabricatedPart> FabricatedParts => Set<FabricatedPart>();

    /// <summary>購入品</summary>
    public DbSet<PurchasedPart> PurchasedParts => Set<PurchasedPart>();

    /// <summary>規格品</summary>
    public DbSet<StandardPart> StandardParts => Set<StandardPart>();

    /// <summary>装置-部品関連（中間テーブル）</summary>
    public DbSet<AssetComponent> AssetComponents => Set<AssetComponent>();

    /// <summary>手配履歴</summary>
    public DbSet<HandoverRecord> HandoverHistory => Set<HandoverRecord>();

    /// <summary>設計基準</summary>
    public DbSet<DesignStandard> Standards => Set<DesignStandard>();

    /// <summary>
    /// パラメータなしコンストラクタ（開発用）。
    /// </summary>
    public DesignAidDbContext() { }

    /// <summary>
    /// オプション付きコンストラクタ（DI 用）。
    /// </summary>
    public DesignAidDbContext(DbContextOptions<DesignAidDbContext> options)
        : base(options)
    {
    }

    /// <inheritdoc />
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            // デフォルトの SQLite データベースパス
            var dataDir = Environment.GetEnvironmentVariable("DA_DATA_DIR")
                ?? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "design-aid");

            Directory.CreateDirectory(dataDir);
            var dbPath = Path.Combine(dataDir, "design_aid.db");

            optionsBuilder.UseSqlite($"Data Source={dbPath}");
        }
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configuration クラスを適用
        modelBuilder.ApplyConfiguration(new ProjectConfiguration());
        modelBuilder.ApplyConfiguration(new AssetConfiguration());
        modelBuilder.ApplyConfiguration(new DesignComponentConfiguration());
        modelBuilder.ApplyConfiguration(new AssetComponentConfiguration());
        modelBuilder.ApplyConfiguration(new HandoverRecordConfiguration());
        modelBuilder.ApplyConfiguration(new DesignStandardConfiguration());
    }
}
