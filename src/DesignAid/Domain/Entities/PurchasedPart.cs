using DesignAid.Domain.ValueObjects;

namespace DesignAid.Domain.Entities;

/// <summary>
/// 購入品を表すエンティティ。
/// メーカーカタログ品やOEM品など、仕様書を正とする。
/// </summary>
public class PurchasedPart : DesignComponent
{
    /// <inheritdoc/>
    public override PartType Type => PartType.Purchased;

    /// <summary>メーカー名</summary>
    public string? Manufacturer { get; set; }

    /// <summary>メーカー型式</summary>
    public string? ManufacturerPartNumber { get; set; }

    /// <summary>仕様書・カタログの相対パス</summary>
    public string? SpecificationPath { get; set; }

    /// <summary>調達リードタイム（日）</summary>
    public int? LeadTimeDays { get; set; }

    /// <summary>単価（参考）</summary>
    public decimal? UnitPrice { get; set; }

    /// <summary>通貨</summary>
    public string? Currency { get; set; }

    /// <summary>
    /// EF Core 用のパラメータなしコンストラクタ。
    /// </summary>
    protected PurchasedPart() { }

    /// <summary>
    /// 新しい購入品を生成する。
    /// </summary>
    /// <param name="partNumber">型式</param>
    /// <param name="name">パーツ名</param>
    /// <param name="directoryPath">ディレクトリパス</param>
    /// <returns>PurchasedPart インスタンス</returns>
    public static PurchasedPart Create(
        PartNumber partNumber,
        string name,
        string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("パーツ名は必須です", nameof(name));

        if (string.IsNullOrWhiteSpace(directoryPath))
            throw new ArgumentException("ディレクトリパスは必須です", nameof(directoryPath));

        var now = DateTime.UtcNow;

        return new PurchasedPart
        {
            Id = Guid.NewGuid(),
            PartNumber = partNumber,
            Name = name,
            DirectoryPath = Path.GetFullPath(directoryPath),
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    /// <summary>
    /// 既存の購入品を再構築する（DB から読み込み時）。
    /// </summary>
    public static PurchasedPart Reconstruct(
        Guid id,
        PartNumber partNumber,
        string name,
        string directoryPath,
        string version,
        string currentHash,
        HandoverStatus status,
        DateTime createdAt,
        DateTime updatedAt,
        string? manufacturer = null,
        string? manufacturerPartNumber = null,
        string? specificationPath = null,
        int? leadTimeDays = null,
        decimal? unitPrice = null,
        string? currency = null)
    {
        return new PurchasedPart
        {
            Id = id,
            PartNumber = partNumber,
            Name = name,
            DirectoryPath = directoryPath,
            Version = version,
            CurrentHash = currentHash,
            Status = status,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
            Manufacturer = manufacturer,
            ManufacturerPartNumber = manufacturerPartNumber,
            SpecificationPath = specificationPath,
            LeadTimeDays = leadTimeDays,
            UnitPrice = unitPrice,
            Currency = currency
        };
    }

    /// <inheritdoc/>
    public override ValidationResult ValidateIntegrity()
    {
        var details = new List<ValidationDetail>();

        // 仕様書ファイルの存在確認
        if (!string.IsNullOrEmpty(SpecificationPath))
        {
            var fullSpecPath = Path.Combine(DirectoryPath, SpecificationPath);
            if (!File.Exists(fullSpecPath))
            {
                details.Add(new ValidationDetail(
                    "SpecificationPath",
                    $"仕様書ファイルが見つかりません: {SpecificationPath}",
                    ValidationSeverity.Warning));
            }
        }

        // 成果物ハッシュの検証
        foreach (var (relativePath, expectedHash) in ArtifactHashes)
        {
            var fullPath = Path.Combine(DirectoryPath, relativePath);
            if (!File.Exists(fullPath))
            {
                details.Add(new ValidationDetail(
                    relativePath,
                    $"成果物ファイルが見つかりません: {relativePath}",
                    ValidationSeverity.Error));
            }
        }

        if (details.Count == 0)
        {
            return ValidationResult.Ok(PartNumber);
        }

        var maxSeverity = details.Max(d => d.Severity);
        return ValidationResult.WithDetails(
            maxSeverity,
            $"購入品 '{PartNumber}' に問題があります",
            details,
            PartNumber);
    }

    /// <summary>
    /// 購入品固有の情報を更新する。
    /// </summary>
    public void UpdatePurchaseInfo(
        string? manufacturer = null,
        string? manufacturerPartNumber = null,
        string? specificationPath = null,
        int? leadTimeDays = null,
        decimal? unitPrice = null,
        string? currency = null)
    {
        Manufacturer = manufacturer ?? Manufacturer;
        ManufacturerPartNumber = manufacturerPartNumber ?? ManufacturerPartNumber;
        SpecificationPath = specificationPath ?? SpecificationPath;
        LeadTimeDays = leadTimeDays ?? LeadTimeDays;
        UnitPrice = unitPrice ?? UnitPrice;
        Currency = currency ?? Currency;
        UpdatedAt = DateTime.UtcNow;
    }
}
