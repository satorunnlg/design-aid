using DesignAid.Application.Services;
using DesignAid.Domain.Entities;
using DesignAid.Domain.Standards;
using DesignAid.Domain.ValueObjects;
using DesignAid.Infrastructure.FileSystem;

namespace DesignAid.Tests.Integration;

/// <summary>
/// サービス間連携の統合テスト。
/// 実際のファイルシステムを使用してワークフロー全体を検証する。
/// </summary>
[Trait("Category", "Integration")]
public class ServiceIntegrationTests : IDisposable
{
    private readonly string _testRoot;
    private readonly HashService _hashService;
    private readonly ProjectMarkerService _projectMarkerService;
    private readonly AssetJsonReader _assetJsonReader;
    private readonly PartJsonReader _partJsonReader;

    public ServiceIntegrationTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"DA_IntegrationTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testRoot);

        _hashService = new HashService();
        _projectMarkerService = new ProjectMarkerService();
        _assetJsonReader = new AssetJsonReader();
        _partJsonReader = new PartJsonReader();
    }

    public void Dispose()
    {
        if (Directory.Exists(_testRoot))
        {
            Directory.Delete(_testRoot, recursive: true);
        }
    }

    /// <summary>
    /// プロジェクト作成 → 装置追加 → パーツ追加 → ハッシュ検証の一連のフローをテスト。
    /// </summary>
    [Fact]
    public async Task FullWorkflow_CreateProjectAndValidateIntegrity()
    {
        // === 1. プロジェクト作成 ===
        var projectPath = Path.Combine(_testRoot, "test-project");
        Directory.CreateDirectory(projectPath);

        var projectId = Guid.NewGuid();
        var projectMarker = await _projectMarkerService.CreateAsync(
            projectPath, projectId, "test-project");

        Assert.Equal(projectId, projectMarker.ProjectId);
        Assert.True(_projectMarkerService.Exists(projectPath));

        // === 2. 装置追加 ===
        var assetPath = Path.Combine(projectPath, "assets", "unit-a");
        Directory.CreateDirectory(assetPath);

        var assetId = Guid.NewGuid();
        var assetJson = await _assetJsonReader.CreateAsync(
            assetPath, assetId, "unit-a", "ユニットA", "テスト装置");

        Assert.Equal(assetId, assetJson.Id);
        Assert.True(_assetJsonReader.Exists(assetPath));
        Assert.True(Directory.Exists(Path.Combine(assetPath, "components")));

        // === 3. パーツ追加 ===
        var partPath = Path.Combine(assetPath, "components", "SP-001");
        Directory.CreateDirectory(partPath);

        // 図面ファイル作成
        var drawingPath = Path.Combine(partPath, "drawing.dxf");
        await File.WriteAllTextAsync(drawingPath, "DXF CONTENT HERE");

        var partId = Guid.NewGuid();
        var partJson = await _partJsonReader.CreateAsync(
            partPath, partId, "SP-001", "テストパーツ", PartType.Fabricated);

        Assert.Equal(partId, partJson.Id);
        Assert.True(_partJsonReader.Exists(partPath));

        // === 4. ハッシュ計算と検証 ===
        var drawingHash = await _hashService.ComputeHashAsync(drawingPath);
        Assert.StartsWith("sha256:", drawingHash.Value);

        // パーツエンティティを作成してハッシュを設定
        var partNumber = new PartNumber("SP-001");
        var part = FabricatedPart.Create(partNumber, "テストパーツ", partPath);
        part.UpdateArtifactHash("drawing.dxf", drawingHash);

        // 整合性検証
        var validationResult = _hashService.ValidateComponentIntegrity(part);
        Assert.True(validationResult.IsSuccess, $"整合性検証失敗: {validationResult.Message}");

        // === 5. ファイル変更後の検証 ===
        await File.WriteAllTextAsync(drawingPath, "MODIFIED DXF CONTENT");

        var validationAfterChange = _hashService.ValidateComponentIntegrity(part);
        Assert.True(validationAfterChange.HasWarnings, "ファイル変更後は警告が出るべき");
        Assert.Contains("ハッシュ不整合", validationAfterChange.Details[0].Message);
    }

    /// <summary>
    /// 既存のサンプルプロジェクトを読み込んでテスト。
    /// </summary>
    [Fact]
    public void ReadSampleProject_CanReadAllJsonFiles()
    {
        // サンプルプロジェクトのパスを計算
        var solutionDir = FindSolutionDirectory();
        if (solutionDir == null)
        {
            // CI環境などでソリューションディレクトリが見つからない場合はスキップ
            return;
        }

        var sampleProjectPath = Path.Combine(solutionDir, "data", "projects", "sample-project");
        if (!Directory.Exists(sampleProjectPath))
        {
            // サンプルプロジェクトが存在しない場合はスキップ
            return;
        }

        // .da-project 読み込み
        var projectMarker = _projectMarkerService.Read(sampleProjectPath);
        Assert.NotNull(projectMarker);
        Assert.Equal("sample-project", projectMarker.Name);

        // asset.json 読み込み
        var assetPath = Path.Combine(sampleProjectPath, "assets", "lifting-unit");
        if (Directory.Exists(assetPath))
        {
            var assetJson = _assetJsonReader.Read(assetPath);
            Assert.NotNull(assetJson);
            Assert.Equal("lifting-unit", assetJson.Name);
        }

        // part.json 読み込み（コンポーネントは共有ディレクトリに配置）
        var partPath = Path.Combine(solutionDir, "data", "components", "SP-TEST-001");
        if (Directory.Exists(partPath))
        {
            var partJson = _partJsonReader.Read(partPath);
            Assert.NotNull(partJson);
            Assert.Equal("SP-TEST-001", partJson.PartNumber);
        }
    }

    /// <summary>
    /// 材料基準のバリデーションテスト。
    /// </summary>
    [Fact]
    public void MaterialStandard_ValidateFabricatedPart()
    {
        // Arrange
        var componentDir = Path.Combine(_testRoot, "material-test");
        Directory.CreateDirectory(componentDir);

        var standard = new MaterialStandard();
        var partNumber = new PartNumber("MAT-001");

        // 承認済み材料
        var approvedPart = FabricatedPart.Create(partNumber, "承認済み部品", componentDir);
        approvedPart.Material = "SS400";

        // 未承認材料
        var unapprovedPart = FabricatedPart.Create(new PartNumber("MAT-002"), "未承認部品", componentDir);
        unapprovedPart.Material = "UNKNOWN-MATERIAL";

        // Act & Assert
        var approvedResult = standard.Validate(approvedPart);
        Assert.True(approvedResult.IsSuccess, "SS400は承認済み材料のはず");

        var unapprovedResult = standard.Validate(unapprovedPart);
        Assert.True(unapprovedResult.HasErrors, "未知の材料はエラーになるはず");
    }

    /// <summary>
    /// ディレクトリ全体のハッシュ計算テスト。
    /// </summary>
    [Fact]
    public void HashService_ComputeDirectoryHashes()
    {
        // Arrange
        var dir = Path.Combine(_testRoot, "hash-dir-test");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "file1.txt"), "content1");
        File.WriteAllText(Path.Combine(dir, "file2.txt"), "content2");
        File.WriteAllText(Path.Combine(dir, "part.json"), "{}"); // 除外されるべき

        // Act
        var hashes = _hashService.ComputeDirectoryHashes(dir);

        // Assert
        Assert.Equal(2, hashes.Count);
        Assert.Contains("file1.txt", hashes.Keys);
        Assert.Contains("file2.txt", hashes.Keys);
        Assert.DoesNotContain("part.json", hashes.Keys);
    }

    /// <summary>
    /// ソリューションディレクトリを検索する。
    /// </summary>
    private static string? FindSolutionDirectory()
    {
        var dir = Directory.GetCurrentDirectory();
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir, "DesignAid.sln")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }
        return null;
    }
}
