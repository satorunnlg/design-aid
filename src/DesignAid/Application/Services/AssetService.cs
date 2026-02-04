using Microsoft.EntityFrameworkCore;
using DesignAid.Domain.Entities;
using DesignAid.Infrastructure.Persistence;

namespace DesignAid.Application.Services;

/// <summary>
/// 装置管理を行うサービス。
/// </summary>
public class AssetService
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
    /// <param name="projectId">プロジェクトID</param>
    /// <param name="name">装置名</param>
    /// <param name="directoryPath">ディレクトリパス</param>
    /// <param name="displayName">表示名</param>
    /// <param name="description">説明</param>
    /// <returns>作成された装置</returns>
    public async Task<Asset> AddAsync(
        Guid projectId,
        string name,
        string directoryPath,
        string? displayName = null,
        string? description = null,
        CancellationToken ct = default)
    {
        // プロジェクト存在チェック
        var project = await _context.Projects
            .FirstOrDefaultAsync(p => p.Id == projectId, ct);

        if (project == null)
        {
            throw new InvalidOperationException($"プロジェクトが見つかりません: {projectId}");
        }

        // 既存チェック
        var existing = await _context.Assets
            .FirstOrDefaultAsync(a => a.ProjectId == projectId && a.Name == name, ct);

        if (existing != null)
        {
            throw new InvalidOperationException($"装置 '{name}' は既に存在します");
        }

        var asset = Asset.Create(projectId, name, directoryPath, displayName, description);
        _context.Assets.Add(asset);
        await _context.SaveChangesAsync(ct);

        return asset;
    }

    /// <summary>
    /// プロジェクト内の装置一覧を取得する。
    /// </summary>
    public async Task<List<Asset>> GetByProjectAsync(Guid projectId, CancellationToken ct = default)
    {
        return await _context.Assets
            .Include(a => a.AssetComponents)
            .Where(a => a.ProjectId == projectId)
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
            .FirstOrDefaultAsync(a => a.Id == id, ct);
    }

    /// <summary>
    /// 装置を名前で取得する（プロジェクト内）。
    /// </summary>
    public async Task<Asset?> GetByNameAsync(
        Guid projectId,
        string name,
        CancellationToken ct = default)
    {
        return await _context.Assets
            .Include(a => a.AssetComponents)
            .FirstOrDefaultAsync(a => a.ProjectId == projectId && a.Name == name, ct);
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
}
