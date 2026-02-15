using Microsoft.EntityFrameworkCore;
using DesignAid.Domain.Entities;
using DesignAid.Infrastructure.Persistence;

namespace DesignAid.Application.Services;

/// <summary>
/// 装置管理を行うサービス。
/// </summary>
public class AssetService : IAssetService
{
    private readonly DesignAidDbContext _context;

    /// <summary>
    /// コンストラクタ。
    /// </summary>
    public AssetService(DesignAidDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// 装置を追加する。
    /// </summary>
    /// <param name="name">装置名（グローバルでユニーク）</param>
    /// <param name="directoryPath">ディレクトリパス</param>
    /// <param name="displayName">表示名</param>
    /// <param name="description">説明</param>
    /// <returns>作成された装置</returns>
    public async Task<Asset> AddAsync(
        string name,
        string directoryPath,
        string? displayName = null,
        string? description = null,
        CancellationToken ct = default)
    {
        // 既存チェック（グローバルでユニーク）
        var existing = await _context.Assets
            .FirstOrDefaultAsync(a => a.Name == name, ct);

        if (existing != null)
        {
            throw new InvalidOperationException($"装置 '{name}' は既に存在します");
        }

        var asset = Asset.Create(name, directoryPath, displayName, description);
        _context.Assets.Add(asset);
        await _context.SaveChangesAsync(ct);

        return asset;
    }

    /// <summary>
    /// 全装置一覧を取得する。
    /// </summary>
    public async Task<List<Asset>> GetAllAsync(CancellationToken ct = default)
    {
        return await _context.Assets
            .Include(a => a.AssetComponents)
            .Include(a => a.ChildAssets)
            .Include(a => a.ParentAssets)
            .OrderBy(a => a.Name)
            .ToListAsync(ct);
    }

    /// <summary>
    /// 装置をIDで取得する。
    /// </summary>
    public async Task<Asset?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _context.Assets
            .Include(a => a.AssetComponents)
            .Include(a => a.ChildAssets)
                .ThenInclude(c => c.ChildAsset)
            .Include(a => a.ParentAssets)
                .ThenInclude(p => p.ParentAsset)
            .FirstOrDefaultAsync(a => a.Id == id, ct);
    }

    /// <summary>
    /// 装置を名前で取得する（グローバルでユニーク）。
    /// </summary>
    public async Task<Asset?> GetByNameAsync(
        string name,
        CancellationToken ct = default)
    {
        return await _context.Assets
            .Include(a => a.AssetComponents)
            .Include(a => a.ChildAssets)
                .ThenInclude(c => c.ChildAsset)
            .Include(a => a.ParentAssets)
                .ThenInclude(p => p.ParentAsset)
            .FirstOrDefaultAsync(a => a.Name == name, ct);
    }

    /// <summary>
    /// 装置を削除する。
    /// </summary>
    public async Task<bool> RemoveAsync(Guid id, CancellationToken ct = default)
    {
        var asset = await _context.Assets
            .FirstOrDefaultAsync(a => a.Id == id, ct);

        if (asset == null)
        {
            return false;
        }

        _context.Assets.Remove(asset);
        await _context.SaveChangesAsync(ct);

        return true;
    }

    /// <summary>
    /// 装置を更新する。
    /// </summary>
    public async Task<Asset?> UpdateAsync(
        Guid id,
        string? displayName = null,
        string? description = null,
        CancellationToken ct = default)
    {
        var asset = await _context.Assets
            .FirstOrDefaultAsync(a => a.Id == id, ct);

        if (asset == null)
        {
            return null;
        }

        asset.Update(displayName, description);
        await _context.SaveChangesAsync(ct);

        return asset;
    }

    /// <summary>
    /// 子装置を追加する（SubAsset として関連付け）。
    /// </summary>
    /// <param name="parentAssetId">親装置ID</param>
    /// <param name="childAssetId">子装置ID</param>
    /// <param name="quantity">数量</param>
    /// <param name="notes">備考</param>
    /// <returns>作成された関連</returns>
    public async Task<AssetSubAsset> AddSubAssetAsync(
        Guid parentAssetId,
        Guid childAssetId,
        int quantity = 1,
        string? notes = null,
        CancellationToken ct = default)
    {
        // 親装置の存在チェック
        var parent = await _context.Assets.FindAsync(new object[] { parentAssetId }, ct);
        if (parent == null)
        {
            throw new InvalidOperationException($"親装置が見つかりません: {parentAssetId}");
        }

        // 子装置の存在チェック
        var child = await _context.Assets.FindAsync(new object[] { childAssetId }, ct);
        if (child == null)
        {
            throw new InvalidOperationException($"子装置が見つかりません: {childAssetId}");
        }

        // 循環参照のチェック
        if (parentAssetId == childAssetId)
        {
            throw new InvalidOperationException("自分自身を子装置として追加できません");
        }

        // 既存の関連チェック
        var existing = await _context.AssetSubAssets
            .FirstOrDefaultAsync(asa =>
                asa.ParentAssetId == parentAssetId && asa.ChildAssetId == childAssetId, ct);

        if (existing != null)
        {
            throw new InvalidOperationException($"装置 '{child.Name}' は既に子装置として登録されています");
        }

        var assetSubAsset = new AssetSubAsset
        {
            ParentAssetId = parentAssetId,
            ChildAssetId = childAssetId,
            Quantity = quantity,
            Notes = notes,
            CreatedAt = DateTime.UtcNow
        };

        _context.AssetSubAssets.Add(assetSubAsset);
        await _context.SaveChangesAsync(ct);

        return assetSubAsset;
    }

    /// <summary>
    /// 子装置を削除する。
    /// </summary>
    public async Task<bool> RemoveSubAssetAsync(
        Guid parentAssetId,
        Guid childAssetId,
        CancellationToken ct = default)
    {
        var subAsset = await _context.AssetSubAssets
            .FirstOrDefaultAsync(asa =>
                asa.ParentAssetId == parentAssetId && asa.ChildAssetId == childAssetId, ct);

        if (subAsset == null)
        {
            return false;
        }

        _context.AssetSubAssets.Remove(subAsset);
        await _context.SaveChangesAsync(ct);

        return true;
    }
}
