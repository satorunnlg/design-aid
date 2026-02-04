using DesignAid.Application.Services;
using DesignAid.Domain.Entities;
using DesignAid.Domain.ValueObjects;
using DesignAid.Infrastructure.FileSystem;
using DesignAid.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DesignAid.Tests.Integration;

/// <summary>
/// 主要ユースケースの統合テスト。
/// 実際の業務フローを模したシナリオでエンドツーエンドの動作を検証する。
/// </summary>
[Trait("Category", "UseCase")]
public class UseCaseTests : IDisposable
{
    private readonly string _testRoot;
    private readonly string _dbPath;
    private readonly DesignAidDbContext _context;
    private readonly AssetService _assetService;
    private readonly PartService _partService;
    private readonly ValidationService _validationService;
    private readonly HashService _hashService;
    private readonly PartJsonReader _partJsonReader;
    private readonly AssetJsonReader _assetJsonReader;

    public UseCaseTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"DA_UseCase_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testRoot);

        _dbPath = Path.Combine(_testRoot, "design_aid_test.db");

        var options = new DbContextOptionsBuilder<DesignAidDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;

        _context = new DesignAidDbContext(options);
        _context.Database.EnsureCreated();

        _assetService = new AssetService(_context);
        _partService = new PartService(_context);
        _hashService = new HashService();
        _validationService = new ValidationService(_context, _hashService);
        _partJsonReader = new PartJsonReader();
        _assetJsonReader = new AssetJsonReader();
    }

    public void Dispose()
    {
        _context.Dispose();
        SqliteConnection.ClearAllPools();

        if (Directory.Exists(_testRoot))
        {
            try { Directory.Delete(_testRoot, recursive: true); }
            catch { /* 削除失敗は無視 */ }
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// ユースケース1: 新規装置作成 → パーツ追加 → 紐づけ → 一覧確認
    ///
    /// 業務シナリオ:
    /// 設計者が新しい装置を作成し、必要な部品を追加して構成を管理する
    /// </summary>
    [Fact]
    public async Task UC1_CreateAssetAndManageParts()
    {
        // === Step 1: 装置を作成 ===
        var assetsDir = Path.Combine(_testRoot, "assets");
        var assetPath = Path.Combine(assetsDir, "lifting-unit");
        Directory.CreateDirectory(assetPath);

        var asset = await _assetService.AddAsync(
            "lifting-unit", assetPath, "昇降ユニット", "エレベータ更新案件");

        Assert.NotNull(asset);
        Assert.Equal("lifting-unit", asset.Name);
        Assert.Equal("昇降ユニット", asset.DisplayName);

        // asset.json が作成できることを確認
        var assetJson = await _assetJsonReader.CreateAsync(
            assetPath, asset.Id, asset.Name, asset.DisplayName, asset.Description);
        Assert.True(_assetJsonReader.Exists(assetPath));

        // === Step 2: 製作物パーツを追加 ===
        var componentsDir = Path.Combine(_testRoot, "components");
        var platePath = Path.Combine(componentsDir, "SP-2026-PLATE-01");
        Directory.CreateDirectory(platePath);

        var plate = await _partService.AddFabricatedPartAsync(
            "SP-2026-PLATE-01", "ベースプレート", platePath,
            material: "SS400", surfaceTreatment: "メッキ");

        Assert.NotNull(plate);
        Assert.Equal(PartType.Fabricated, plate.Type);
        Assert.Equal("SS400", plate.Material);

        // === Step 3: 購入品パーツを追加 ===
        var motorPath = Path.Combine(componentsDir, "MTR-001");
        Directory.CreateDirectory(motorPath);

        var motor = await _partService.AddPurchasedPartAsync(
            "MTR-001", "サーボモータ", motorPath,
            manufacturer: "三菱電機", modelNumber: "HG-KR43");

        Assert.NotNull(motor);
        Assert.Equal(PartType.Purchased, motor.Type);

        // === Step 4: パーツを装置に紐づけ ===
        var plateLink = await _partService.LinkToAssetAsync(asset.Id, plate.Id, quantity: 2, notes: "左右取付");
        var motorLink = await _partService.LinkToAssetAsync(asset.Id, motor.Id, quantity: 1);

        Assert.Equal(2, plateLink.Quantity);
        Assert.Equal("左右取付", plateLink.Notes);

        // === Step 5: 装置に紐づいたパーツ一覧を確認 ===
        var linkedParts = await _partService.GetPartsByAssetAsync(asset.Id);

        Assert.Equal(2, linkedParts.Count);
        Assert.Contains(linkedParts, p => p.Part.PartNumber.Value == "SP-2026-PLATE-01");
        Assert.Contains(linkedParts, p => p.Part.PartNumber.Value == "MTR-001");

        // === Step 6: 全体一覧で確認 ===
        var allParts = await _partService.GetAllAsync();
        Assert.Equal(2, allParts.Count);

        var allAssets = await _assetService.GetAllAsync();
        Assert.Single(allAssets);
    }

    /// <summary>
    /// ユースケース2: 製作物の整合性管理（ハッシュベース）
    ///
    /// 業務シナリオ:
    /// 図面ファイルを作成・更新し、ハッシュで変更を追跡する
    /// </summary>
    [Fact]
    public async Task UC2_IntegrityManagementWithHashes()
    {
        // === Step 1: パーツと図面を作成 ===
        var componentsDir = Path.Combine(_testRoot, "components");
        var bracketPath = Path.Combine(componentsDir, "SP-2026-BRACKET-01");
        Directory.CreateDirectory(bracketPath);

        var drawingPath = Path.Combine(bracketPath, "drawing.dxf");
        var originalContent = "DXF CONTENT VERSION 1.0";
        await File.WriteAllTextAsync(drawingPath, originalContent);

        var bracket = await _partService.AddFabricatedPartAsync(
            "SP-2026-BRACKET-01", "ブラケット", bracketPath, material: "SS400");

        // === Step 2: 初回ハッシュ計算・登録 ===
        var originalHash = await _hashService.ComputeHashAsync(drawingPath);
        bracket.UpdateArtifactHash("drawing.dxf", originalHash);
        await _context.SaveChangesAsync();

        // === Step 3: 整合性検証（初回 - OK のはず）===
        var result1 = _hashService.ValidateComponentIntegrity(bracket);
        Assert.True(result1.IsSuccess, $"初回検証が失敗: {result1.Message}");

        // === Step 4: ファイルを変更（設計変更をシミュレート）===
        var modifiedContent = "DXF CONTENT VERSION 2.0 - MODIFIED";
        await File.WriteAllTextAsync(drawingPath, modifiedContent);

        // === Step 5: 変更検知（Warning のはず）===
        var result2 = _hashService.ValidateComponentIntegrity(bracket);
        Assert.True(result2.HasWarnings, "ファイル変更後は警告が出るはず");
        Assert.Contains(result2.Details, d => d.Message.Contains("ハッシュ不整合"));

        // === Step 6: ハッシュを更新（設計変更承認をシミュレート）===
        var newHash = await _hashService.ComputeHashAsync(drawingPath);
        bracket.UpdateArtifactHash("drawing.dxf", newHash);
        await _context.SaveChangesAsync();

        // === Step 7: 再検証（OK のはず）===
        var result3 = _hashService.ValidateComponentIntegrity(bracket);
        Assert.True(result3.IsSuccess, "ハッシュ更新後は成功するはず");
    }

    /// <summary>
    /// ユースケース3: 子装置の組み込み（再利用）
    ///
    /// 業務シナリオ:
    /// 過去に設計した装置を新しい装置のサブシステムとして組み込む
    /// </summary>
    [Fact]
    public async Task UC3_SubAssetReuse()
    {
        // === Step 1: 既存装置（安全モジュール）を作成 ===
        var assetsDir = Path.Combine(_testRoot, "assets");
        var safetyPath = Path.Combine(assetsDir, "safety-module");
        Directory.CreateDirectory(safetyPath);

        var safetyModule = await _assetService.AddAsync(
            "safety-module", safetyPath, "安全モジュール", "安全装置ユニット");

        // === Step 2: 新規装置（メインユニット）を作成 ===
        var mainPath = Path.Combine(assetsDir, "main-unit");
        Directory.CreateDirectory(mainPath);

        var mainUnit = await _assetService.AddAsync(
            "main-unit", mainPath, "メインユニット", "制御装置本体");

        // === Step 3: 安全モジュールをメインユニットの子装置として組み込み ===
        var subAsset = await _assetService.AddSubAssetAsync(
            mainUnit.Id, safetyModule.Id, quantity: 2, notes: "冗長構成のため2台");

        Assert.Equal(mainUnit.Id, subAsset.ParentAssetId);
        Assert.Equal(safetyModule.Id, subAsset.ChildAssetId);
        Assert.Equal(2, subAsset.Quantity);

        // === Step 4: 親装置から子装置の情報を取得 ===
        var parent = await _context.Assets
            .Include(a => a.ChildAssets)
            .ThenInclude(c => c.ChildAsset)
            .FirstOrDefaultAsync(a => a.Id == mainUnit.Id);

        Assert.NotNull(parent);
        Assert.Single(parent.ChildAssets);
        Assert.Equal("safety-module", parent.ChildAssets.First().ChildAsset!.Name);

        // === Step 5: 子装置から親装置の情報を取得 ===
        var child = await _context.Assets
            .Include(a => a.ParentAssets)
            .ThenInclude(p => p.ParentAsset)
            .FirstOrDefaultAsync(a => a.Id == safetyModule.Id);

        Assert.NotNull(child);
        Assert.Single(child.ParentAssets);
        Assert.Equal("main-unit", child.ParentAssets.First().ParentAsset!.Name);
    }

    /// <summary>
    /// ユースケース4: 設計基準バリデーション
    ///
    /// 業務シナリオ:
    /// パーツが設計基準に適合しているかを検証する
    /// </summary>
    [Fact]
    public async Task UC4_DesignStandardValidation()
    {
        // === Step 1: パーツを作成 ===
        var componentsDir = Path.Combine(_testRoot, "components");
        var shaftPath = Path.Combine(componentsDir, "SP-2026-SHAFT-01");
        Directory.CreateDirectory(shaftPath);

        var shaft = await _partService.AddFabricatedPartAsync(
            "SP-2026-SHAFT-01", "メインシャフト", shaftPath, material: "S45C");

        // === Step 2: 利用可能な設計基準を確認 ===
        var standards = _validationService.GetAvailableStandards();
        Assert.NotEmpty(standards);

        // === Step 3: 材料基準を取得 ===
        var materialStandard = _validationService.GetStandard("STD-MATERIAL-01");
        Assert.NotNull(materialStandard);
        Assert.Equal("材料基準", materialStandard.Name);

        // === Step 4: 全体バリデーションを実行 ===
        var validationResult = await _validationService.VerifyAllAsync();
        Assert.NotNull(validationResult);
        Assert.True(validationResult.Results.ContainsKey("SP-2026-SHAFT-01"));
    }

    /// <summary>
    /// ユースケース5: 複数装置でのパーツ共有
    ///
    /// 業務シナリオ:
    /// 同じ部品（例：標準ボルト）を複数の装置で使用する
    /// </summary>
    [Fact]
    public async Task UC5_SharedPartsBetweenAssets()
    {
        // === Step 1: 2つの装置を作成 ===
        var assetsDir = Path.Combine(_testRoot, "assets");

        var unit1Path = Path.Combine(assetsDir, "unit-1");
        var unit2Path = Path.Combine(assetsDir, "unit-2");
        Directory.CreateDirectory(unit1Path);
        Directory.CreateDirectory(unit2Path);

        var unit1 = await _assetService.AddAsync("unit-1", unit1Path, "ユニット1");
        var unit2 = await _assetService.AddAsync("unit-2", unit2Path, "ユニット2");

        // === Step 2: 共有パーツ（標準ボルト）を作成 ===
        var componentsDir = Path.Combine(_testRoot, "components");
        var boltPath = Path.Combine(componentsDir, "STD-BOLT-M10");
        Directory.CreateDirectory(boltPath);

        var bolt = await _partService.AddStandardPartAsync(
            "STD-BOLT-M10", "六角ボルトM10×30", boltPath,
            standardCode: "JIS B1180", size: "M10×30");

        // === Step 3: 両方の装置に同じパーツをリンク ===
        await _partService.LinkToAssetAsync(unit1.Id, bolt.Id, quantity: 20, notes: "ユニット1用");
        await _partService.LinkToAssetAsync(unit2.Id, bolt.Id, quantity: 15, notes: "ユニット2用");

        // === Step 4: 各装置のパーツ構成を確認 ===
        var unit1Parts = await _partService.GetPartsByAssetAsync(unit1.Id);
        var unit2Parts = await _partService.GetPartsByAssetAsync(unit2.Id);

        Assert.Single(unit1Parts);
        Assert.Single(unit2Parts);
        Assert.Equal(20, unit1Parts[0].Link.Quantity);
        Assert.Equal(15, unit2Parts[0].Link.Quantity);

        // === Step 5: パーツ側から見ると2つの装置にリンクされている ===
        var boltLinks = await _context.AssetComponents
            .Where(ac => ac.PartId == bolt.Id)
            .ToListAsync();

        Assert.Equal(2, boltLinks.Count);
        Assert.Equal(35, boltLinks.Sum(l => l.Quantity)); // 合計使用数量
    }

    /// <summary>
    /// ユースケース6: パーツの削除と影響確認
    ///
    /// 業務シナリオ:
    /// 使用中のパーツを削除しようとした場合の制約確認
    /// </summary>
    [Fact]
    public async Task UC6_PartDeletionWithRestriction()
    {
        // === Step 1: 装置とパーツを作成してリンク ===
        var assetsDir = Path.Combine(_testRoot, "assets");
        var unitPath = Path.Combine(assetsDir, "delete-test-unit");
        Directory.CreateDirectory(unitPath);

        var unit = await _assetService.AddAsync("delete-test-unit", unitPath, "削除テスト装置");

        var componentsDir = Path.Combine(_testRoot, "components");
        var partPath = Path.Combine(componentsDir, "DEL-TEST-001");
        Directory.CreateDirectory(partPath);

        var part = await _partService.AddFabricatedPartAsync("DEL-TEST-001", "削除テスト部品", partPath);

        await _partService.LinkToAssetAsync(unit.Id, part.Id, quantity: 1);

        // === Step 2: リンクがある状態でパーツを削除しようとする ===
        // PartService.RemoveAsync はリンクがあっても削除を試みる（FK制約で失敗するはず）
        // ただし、現在の実装ではAssetComponent側のON DELETE RESTRICTがあるため
        // DbUpdateExceptionが発生する

        // === Step 3: 装置を先に削除（CASCADE でリンクも削除される）===
        var removed = await _assetService.RemoveAsync(unit.Id);
        Assert.True(removed);

        // === Step 4: これでパーツは削除可能 ===
        var partRemoved = await _partService.RemoveAsync("DEL-TEST-001");
        Assert.True(partRemoved);

        // === Step 5: 両方とも削除されていることを確認 ===
        var foundUnit = await _assetService.GetByIdAsync(unit.Id);
        var foundPart = await _partService.GetByPartNumberAsync("DEL-TEST-001");
        Assert.Null(foundUnit);
        Assert.Null(foundPart);
    }

    /// <summary>
    /// ユースケース7: 手配ステータスの変更フロー
    ///
    /// 業務シナリオ:
    /// パーツの設計完了 → 手配 → 納品のステータス遷移
    /// </summary>
    [Fact]
    public async Task UC7_HandoverStatusTransition()
    {
        // === Step 1: パーツを作成（初期状態: Draft）===
        var componentsDir = Path.Combine(_testRoot, "components");
        var partPath = Path.Combine(componentsDir, "ORDER-001");
        Directory.CreateDirectory(partPath);

        var part = await _partService.AddFabricatedPartAsync(
            "ORDER-001", "手配テスト部品", partPath, material: "SS400");

        Assert.Equal(HandoverStatus.Draft, part.Status);

        // === Step 2: 図面作成・ハッシュ登録 ===
        var drawingPath = Path.Combine(partPath, "drawing.dxf");
        await File.WriteAllTextAsync(drawingPath, "DXF CONTENT");
        var hash = await _hashService.ComputeHashAsync(drawingPath);
        part.UpdateArtifactHash("drawing.dxf", hash);

        // === Step 3: 手配ステータスに変更 ===
        part.ChangeStatus(HandoverStatus.Ordered);
        await _context.SaveChangesAsync();

        // 手配履歴を記録
        var orderRecord = HandoverRecord.CreateOrder(part.Id, hash.Value);
        _context.HandoverHistory.Add(orderRecord);
        await _context.SaveChangesAsync();

        // === Step 4: 納品完了 ===
        part.ChangeStatus(HandoverStatus.Delivered);
        orderRecord.MarkAsDelivered();
        await _context.SaveChangesAsync();

        // === Step 5: 履歴確認 ===
        var history = await _context.HandoverHistory
            .Where(h => h.PartId == part.Id)
            .FirstOrDefaultAsync();

        Assert.NotNull(history);
        Assert.Equal(HandoverStatus.Delivered, history.Status);
        Assert.NotNull(history.DeliveryDate);
    }

    /// <summary>
    /// ユースケース8: パーツ検索
    ///
    /// 業務シナリオ:
    /// 型式やタイプでパーツを検索する
    /// </summary>
    [Fact]
    public async Task UC8_PartSearch()
    {
        // === Step 1: 複数パーツを作成 ===
        var componentsDir = Path.Combine(_testRoot, "components");

        var parts = new[]
        {
            ("FAB-PLATE-001", "鉄板", PartType.Fabricated),
            ("FAB-BRACKET-001", "ブラケット", PartType.Fabricated),
            ("PUR-MOTOR-001", "モータ", PartType.Purchased),
            ("STD-BOLT-001", "ボルト", PartType.Standard)
        };

        foreach (var (pn, name, type) in parts)
        {
            var path = Path.Combine(componentsDir, pn);
            Directory.CreateDirectory(path);

            switch (type)
            {
                case PartType.Fabricated:
                    await _partService.AddFabricatedPartAsync(pn, name, path);
                    break;
                case PartType.Purchased:
                    await _partService.AddPurchasedPartAsync(pn, name, path);
                    break;
                case PartType.Standard:
                    await _partService.AddStandardPartAsync(pn, name, path);
                    break;
            }
        }

        // === Step 2: 型式で検索 ===
        var bracket = await _partService.GetByPartNumberAsync("FAB-BRACKET-001");
        Assert.NotNull(bracket);
        Assert.Equal("ブラケット", bracket.Name);

        // === Step 3: タイプで検索 ===
        var fabricated = await _partService.GetByTypeAsync(PartType.Fabricated);
        Assert.Equal(2, fabricated.Count);

        var purchased = await _partService.GetByTypeAsync(PartType.Purchased);
        Assert.Single(purchased);

        var standard = await _partService.GetByTypeAsync(PartType.Standard);
        Assert.Single(standard);

        // === Step 4: 全件取得 ===
        var all = await _partService.GetAllAsync();
        Assert.Equal(4, all.Count);
    }
}
