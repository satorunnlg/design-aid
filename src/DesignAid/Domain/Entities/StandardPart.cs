using DesignAid.Domain.ValueObjects;

namespace DesignAid.Domain.Entities;

/// <summary>
/// 規格品を表すエンティティ。
/// JIS、ISO、DIN などの規格で定義された標準部品。
/// </summary>
public class StandardPart : DesignComponent
{
    /// <inheritdoc/>
    public override PartType Type => PartType.Standard;

    /// <summary>規格名（例: JIS, ISO, DIN）</summary>
    public string? StandardName { get; set; }

    /// <summary>規格番号（例: JIS B 1180）</summary>
    public string? StandardNumber { get; set; }

    /// <summary>サイズ/寸法（例: M10x30）</summary>
    public string? Size { get; set; }

    /// <summary>材質グレード（例: A2-70）</summary>
    public string? MaterialGrade { get; set; }

    /// <summary>仕上げ（例: ユニクロ、黒染め）</summary>
    public string? Finish { get; set; }

    /// <summary>
    /// EF Core 用のパラメータなしコンストラクタ。
    /// </summary>
    protected StandardPart() { }

    /// <summary>
    /// 新しい規格品を生成する。
    /// </summary>
    /// <param name="partNumber">型式</param>
    /// <param name="name">パーツ名</param>
    /// <param name="directoryPath">ディレクトリパス</param>
    /// <returns>StandardPart インスタンス</returns>
    public static StandardPart Create(
        PartNumber partNumber,
        string name,
        string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("パーツ名は必須です", nameof(name));

        if (string.IsNullOrWhiteSpace(directoryPath))
            throw new ArgumentException("ディレクトリパスは必須です", nameof(directoryPath));

        var now = DateTime.UtcNow;

        return new StandardPart
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
    /// 既存の規格品を再構築する（DB から読み込み時）。
    /// </summary>
    public static StandardPart Reconstruct(
        Guid id,
        PartNumber partNumber,
        string name,
        string directoryPath,
        string version,
        string currentHash,
        HandoverStatus status,
        DateTime createdAt,
        DateTime updatedAt,
        string? standardName = null,
        string? standardNumber = null,
        string? size = null,
        string? materialGrade = null,
        string? finish = null)
    {
        return new StandardPart
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
            StandardName = standardName,
            StandardNumber = standardNumber,
            Size = size,
            MaterialGrade = materialGrade,
            Finish = finish
        };
    }

    /// <inheritdoc/>
    public override ValidationResult ValidateIntegrity()
    {
        var details = new List<ValidationDetail>();

        // 規格品は成果物が少ないことが多いため、基本的な検証のみ
        foreach (var (relativePath, expectedHash) in ArtifactHashes)
        {
            var fullPath = Path.Combine(DirectoryPath, relativePath);
            if (!File.Exists(fullPath))
            {
                details.Add(new ValidationDetail(
                    relativePath,
                    $"成果物ファイルが見つかりません: {relativePath}",
                    ValidationSeverity.Warning));
            }
        }

        if (details.Count == 0)
        {
            return ValidationResult.Ok(PartNumber);
        }

        var maxSeverity = details.Max(d => d.Severity);
        return ValidationResult.WithDetails(
            maxSeverity,
            $"規格品 '{PartNumber}' に問題があります",
            details,
            PartNumber);
    }

    /// <summary>
    /// 規格品固有の情報を更新する。
    /// </summary>
    public void UpdateStandardInfo(
        string? standardName = null,
        string? standardNumber = null,
        string? size = null,
        string? materialGrade = null,
        string? finish = null)
    {
        StandardName = standardName ?? StandardName;
        StandardNumber = standardNumber ?? StandardNumber;
        Size = size ?? Size;
        MaterialGrade = materialGrade ?? MaterialGrade;
        Finish = finish ?? Finish;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// 規格品の完全な仕様文字列を取得する。
    /// 例: "JIS B 1180 M10x30 A2-70 ユニクロ"
    /// </summary>
    public string GetFullSpecification()
    {
        var parts = new List<string>();

        if (!string.IsNullOrEmpty(StandardName) && !string.IsNullOrEmpty(StandardNumber))
            parts.Add($"{StandardName} {StandardNumber}");
        else if (!string.IsNullOrEmpty(StandardNumber))
            parts.Add(StandardNumber);

        if (!string.IsNullOrEmpty(Size))
            parts.Add(Size);

        if (!string.IsNullOrEmpty(MaterialGrade))
            parts.Add(MaterialGrade);

        if (!string.IsNullOrEmpty(Finish))
            parts.Add(Finish);

        return string.Join(" ", parts);
    }
}
