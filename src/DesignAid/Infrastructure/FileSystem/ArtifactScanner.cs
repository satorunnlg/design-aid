namespace DesignAid.Infrastructure.FileSystem;

/// <summary>
/// 成果物（図面、計算書等）をスキャンするサービス。
/// </summary>
public class ArtifactScanner
{
    /// <summary>
    /// 対応する成果物ファイルの拡張子。
    /// </summary>
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        // 図面
        ".dxf", ".dwg", ".pdf",
        // 3D CAD（将来対応）
        ".step", ".stp", ".iges", ".igs",
        // ドキュメント
        ".xlsx", ".xls", ".docx", ".doc",
        // 画像
        ".png", ".jpg", ".jpeg", ".bmp", ".tiff"
    };

    /// <summary>
    /// 除外するファイル名パターン。
    /// </summary>
    private static readonly HashSet<string> ExcludedFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "part.json",
        "asset.json",
        ".da-project",
        "Thumbs.db",
        ".DS_Store"
    };

    /// <summary>
    /// ディレクトリ内の成果物をスキャンする。
    /// </summary>
    /// <param name="directoryPath">スキャン対象ディレクトリ</param>
    /// <param name="includeSubdirectories">サブディレクトリも含むか</param>
    /// <returns>成果物ファイルのパスリスト（相対パス）</returns>
    public IReadOnlyList<string> ScanArtifacts(
        string directoryPath,
        bool includeSubdirectories = false)
    {
        if (!Directory.Exists(directoryPath))
        {
            return [];
        }

        var searchOption = includeSubdirectories
            ? SearchOption.AllDirectories
            : SearchOption.TopDirectoryOnly;

        return Directory.GetFiles(directoryPath, "*.*", searchOption)
            .Where(IsArtifactFile)
            .Select(f => Path.GetRelativePath(directoryPath, f))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// ディレクトリ内の成果物をスキャンし、詳細情報を取得する。
    /// </summary>
    /// <param name="directoryPath">スキャン対象ディレクトリ</param>
    /// <param name="includeSubdirectories">サブディレクトリも含むか</param>
    /// <returns>成果物詳細情報リスト</returns>
    public IReadOnlyList<ArtifactInfo> ScanArtifactsWithInfo(
        string directoryPath,
        bool includeSubdirectories = false)
    {
        if (!Directory.Exists(directoryPath))
        {
            return [];
        }

        var searchOption = includeSubdirectories
            ? SearchOption.AllDirectories
            : SearchOption.TopDirectoryOnly;

        return Directory.GetFiles(directoryPath, "*.*", searchOption)
            .Where(IsArtifactFile)
            .Select(f => CreateArtifactInfo(f, directoryPath))
            .OrderBy(a => a.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// ファイルが成果物として扱われるかを判定する。
    /// </summary>
    public bool IsArtifactFile(string filePath)
    {
        var fileName = Path.GetFileName(filePath);

        // 除外ファイル名チェック
        if (ExcludedFileNames.Contains(fileName))
        {
            return false;
        }

        // 隠しファイルチェック（.で始まるファイル）
        if (fileName.StartsWith('.'))
        {
            return false;
        }

        // 拡張子チェック
        var extension = Path.GetExtension(filePath);
        return SupportedExtensions.Contains(extension);
    }

    /// <summary>
    /// 指定した拡張子のファイルのみをスキャンする。
    /// </summary>
    /// <param name="directoryPath">スキャン対象ディレクトリ</param>
    /// <param name="extensions">対象拡張子（例: ".dxf", ".pdf"）</param>
    /// <param name="includeSubdirectories">サブディレクトリも含むか</param>
    /// <returns>ファイルパスリスト（相対パス）</returns>
    public IReadOnlyList<string> ScanByExtensions(
        string directoryPath,
        IEnumerable<string> extensions,
        bool includeSubdirectories = false)
    {
        if (!Directory.Exists(directoryPath))
        {
            return [];
        }

        var extensionSet = new HashSet<string>(
            extensions.Select(e => e.StartsWith('.') ? e : $".{e}"),
            StringComparer.OrdinalIgnoreCase);

        var searchOption = includeSubdirectories
            ? SearchOption.AllDirectories
            : SearchOption.TopDirectoryOnly;

        return Directory.GetFiles(directoryPath, "*.*", searchOption)
            .Where(f => extensionSet.Contains(Path.GetExtension(f)))
            .Where(f => !ExcludedFileNames.Contains(Path.GetFileName(f)))
            .Select(f => Path.GetRelativePath(directoryPath, f))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// 図面ファイルのみをスキャンする。
    /// </summary>
    public IReadOnlyList<string> ScanDrawings(
        string directoryPath,
        bool includeSubdirectories = false)
    {
        return ScanByExtensions(
            directoryPath,
            [".dxf", ".dwg", ".pdf"],
            includeSubdirectories);
    }

    /// <summary>
    /// 3D CAD ファイルのみをスキャンする。
    /// </summary>
    public IReadOnlyList<string> Scan3dModels(
        string directoryPath,
        bool includeSubdirectories = false)
    {
        return ScanByExtensions(
            directoryPath,
            [".step", ".stp", ".iges", ".igs"],
            includeSubdirectories);
    }

    /// <summary>
    /// ドキュメントファイルのみをスキャンする。
    /// </summary>
    public IReadOnlyList<string> ScanDocuments(
        string directoryPath,
        bool includeSubdirectories = false)
    {
        return ScanByExtensions(
            directoryPath,
            [".xlsx", ".xls", ".docx", ".doc", ".pdf"],
            includeSubdirectories);
    }

    /// <summary>
    /// 成果物詳細情報を作成する。
    /// </summary>
    private static ArtifactInfo CreateArtifactInfo(string fullPath, string basePath)
    {
        var fileInfo = new FileInfo(fullPath);
        return new ArtifactInfo
        {
            FullPath = fullPath,
            RelativePath = Path.GetRelativePath(basePath, fullPath),
            FileName = fileInfo.Name,
            Extension = fileInfo.Extension,
            Size = fileInfo.Length,
            LastModified = fileInfo.LastWriteTimeUtc,
            Category = CategorizeByExtension(fileInfo.Extension)
        };
    }

    /// <summary>
    /// 拡張子からカテゴリを判定する。
    /// </summary>
    private static ArtifactCategory CategorizeByExtension(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".dxf" or ".dwg" => ArtifactCategory.Drawing,
            ".pdf" => ArtifactCategory.Document,
            ".step" or ".stp" or ".iges" or ".igs" => ArtifactCategory.Model3D,
            ".xlsx" or ".xls" => ArtifactCategory.Spreadsheet,
            ".docx" or ".doc" => ArtifactCategory.Document,
            ".png" or ".jpg" or ".jpeg" or ".bmp" or ".tiff" => ArtifactCategory.Image,
            _ => ArtifactCategory.Other
        };
    }
}

/// <summary>
/// 成果物の詳細情報。
/// </summary>
public class ArtifactInfo
{
    /// <summary>フルパス</summary>
    public required string FullPath { get; init; }

    /// <summary>相対パス</summary>
    public required string RelativePath { get; init; }

    /// <summary>ファイル名</summary>
    public required string FileName { get; init; }

    /// <summary>拡張子</summary>
    public required string Extension { get; init; }

    /// <summary>ファイルサイズ（バイト）</summary>
    public long Size { get; init; }

    /// <summary>最終更新日時（UTC）</summary>
    public DateTime LastModified { get; init; }

    /// <summary>カテゴリ</summary>
    public ArtifactCategory Category { get; init; }
}

/// <summary>
/// 成果物のカテゴリ。
/// </summary>
public enum ArtifactCategory
{
    /// <summary>図面（DXF, DWG）</summary>
    Drawing,

    /// <summary>3Dモデル（STEP, IGES）</summary>
    Model3D,

    /// <summary>ドキュメント（PDF, Word）</summary>
    Document,

    /// <summary>スプレッドシート（Excel）</summary>
    Spreadsheet,

    /// <summary>画像</summary>
    Image,

    /// <summary>その他</summary>
    Other
}
