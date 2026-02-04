using Microsoft.EntityFrameworkCore;
using DesignAid.Domain.Entities;
using DesignAid.Infrastructure.Persistence;

namespace DesignAid.Application.Services;

/// <summary>
/// プロジェクト管理を行うサービス。
/// </summary>
public class ProjectService
{
    private readonly DesignAidDbContext _context;

    /// <summary>
    /// コンストラクタ。
    /// </summary>
    public ProjectService(DesignAidDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// プロジェクトを追加する。
    /// </summary>
    /// <param name="name">プロジェクト名</param>
    /// <param name="directoryPath">ディレクトリパス</param>
    /// <param name="displayName">表示名</param>
    /// <param name="description">説明</param>
    /// <returns>作成されたプロジェクト</returns>
    public async Task<Project> AddAsync(
        string name,
        string directoryPath,
        string? displayName = null,
        string? description = null,
        CancellationToken ct = default)
    {
        // 既存チェック
        var existing = await _context.Projects
            .FirstOrDefaultAsync(p => p.Name == name, ct);

        if (existing != null)
        {
            throw new InvalidOperationException($"プロジェクト '{name}' は既に登録されています");
        }

        var project = Project.Create(name, directoryPath, displayName, description);
        _context.Projects.Add(project);
        await _context.SaveChangesAsync(ct);

        return project;
    }

    /// <summary>
    /// プロジェクト一覧を取得する。
    /// </summary>
    public async Task<List<Project>> GetAllAsync(CancellationToken ct = default)
    {
        return await _context.Projects
            .Include(p => p.Assets)
            .OrderBy(p => p.Name)
            .ToListAsync(ct);
    }

    /// <summary>
    /// プロジェクトを名前で取得する。
    /// </summary>
    public async Task<Project?> GetByNameAsync(string name, CancellationToken ct = default)
    {
        return await _context.Projects
            .Include(p => p.Assets)
            .FirstOrDefaultAsync(p => p.Name == name, ct);
    }

    /// <summary>
    /// プロジェクトをIDで取得する。
    /// </summary>
    public async Task<Project?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _context.Projects
            .Include(p => p.Assets)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
    }

    /// <summary>
    /// プロジェクトを削除する。
    /// </summary>
    public async Task<bool> RemoveAsync(string name, CancellationToken ct = default)
    {
        var project = await _context.Projects
            .FirstOrDefaultAsync(p => p.Name == name, ct);

        if (project == null)
        {
            return false;
        }

        _context.Projects.Remove(project);
        await _context.SaveChangesAsync(ct);

        return true;
    }

    /// <summary>
    /// プロジェクトを更新する。
    /// </summary>
    public async Task<Project?> UpdateAsync(
        string name,
        string? displayName = null,
        string? description = null,
        CancellationToken ct = default)
    {
        var project = await _context.Projects
            .FirstOrDefaultAsync(p => p.Name == name, ct);

        if (project == null)
        {
            return null;
        }

        project.Update(displayName, description);
        await _context.SaveChangesAsync(ct);

        return project;
    }
}
