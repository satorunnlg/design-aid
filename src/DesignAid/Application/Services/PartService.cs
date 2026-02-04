using Microsoft.EntityFrameworkCore;
using DesignAid.Domain.Entities;
using DesignAid.Domain.ValueObjects;
using DesignAid.Infrastructure.Persistence;

namespace DesignAid.Application.Services;

/// <summary>
/// パーツ管理を行うサービス。
/// </summary>
public class PartService
{
    private readonly DesignAidDbContext _context;

    /// <summary>
    /// コンストラクタ。
    /// </summary>
    public PartService(DesignAidDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// 製作物パーツを追加する。
    /// </summary>
    public async Task<FabricatedPart> AddFabricatedPartAsync(
        string partNumber,
        string name,
        string directoryPath,
        string? material = null,
        string? surfaceTreatment = null,
        string? memo = null,
        CancellationToken ct = default)
    {
        // 既存チェック
        var pn = new PartNumber(partNumber);
        var existing = await _context.Parts
            .FirstOrDefaultAsync(p => p.PartNumber == pn, ct);

        if (existing != null)
        {
            throw new InvalidOperationException($"パーツ '{partNumber}' は既に存在します");
        }

        var part = FabricatedPart.Create(pn, name, directoryPath);
        part.Material = material;
        part.SurfaceTreatment = surfaceTreatment;
        part.Memo = memo;

        _context.FabricatedParts.Add(part);
        await _context.SaveChangesAsync(ct);

        return part;
    }

    /// <summary>
    /// 購入品パーツを追加する。
    /// </summary>
    public async Task<PurchasedPart> AddPurchasedPartAsync(
        string partNumber,
        string name,
        string directoryPath,
        string? manufacturer = null,
        string? modelNumber = null,
        string? memo = null,
        CancellationToken ct = default)
    {
        // 既存チェック
        var pn = new PartNumber(partNumber);
        var existing = await _context.Parts
            .FirstOrDefaultAsync(p => p.PartNumber == pn, ct);

        if (existing != null)
        {
            throw new InvalidOperationException($"パーツ '{partNumber}' は既に存在します");
        }

        var part = PurchasedPart.Create(pn, name, directoryPath);
        part.Manufacturer = manufacturer;
        part.ManufacturerPartNumber = modelNumber;
        part.Memo = memo;

        _context.PurchasedParts.Add(part);
        await _context.SaveChangesAsync(ct);

        return part;
    }

    /// <summary>
    /// 規格品パーツを追加する。
    /// </summary>
    public async Task<StandardPart> AddStandardPartAsync(
        string partNumber,
        string name,
        string directoryPath,
        string? standardCode = null,
        string? size = null,
        string? memo = null,
        CancellationToken ct = default)
    {
        // 既存チェック
        var pn = new PartNumber(partNumber);
        var existing = await _context.Parts
            .FirstOrDefaultAsync(p => p.PartNumber == pn, ct);

        if (existing != null)
        {
            throw new InvalidOperationException($"パーツ '{partNumber}' は既に存在します");
        }

        var part = StandardPart.Create(pn, name, directoryPath);
        part.StandardNumber = standardCode;
        part.Size = size;
        part.Memo = memo;

        _context.StandardParts.Add(part);
        await _context.SaveChangesAsync(ct);

        return part;
    }

    /// <summary>
    /// パーツ一覧を取得する。
    /// </summary>
    public async Task<List<DesignComponent>> GetAllAsync(CancellationToken ct = default)
    {
        return await _context.Parts
            .OrderBy(p => p.PartNumber)
            .ToListAsync(ct);
    }

    /// <summary>
    /// 種別でパーツを取得する。
    /// </summary>
    public async Task<List<DesignComponent>> GetByTypeAsync(
        PartType type,
        CancellationToken ct = default)
    {
        return type switch
        {
            PartType.Fabricated => await _context.FabricatedParts
                .OrderBy(p => p.PartNumber)
                .Cast<DesignComponent>()
                .ToListAsync(ct),
            PartType.Purchased => await _context.PurchasedParts
                .OrderBy(p => p.PartNumber)
                .Cast<DesignComponent>()
                .ToListAsync(ct),
            PartType.Standard => await _context.StandardParts
                .OrderBy(p => p.PartNumber)
                .Cast<DesignComponent>()
                .ToListAsync(ct),
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };
    }

    /// <summary>
    /// パーツを型式で取得する。
    /// </summary>
    public async Task<DesignComponent?> GetByPartNumberAsync(
        string partNumber,
        CancellationToken ct = default)
    {
        return await _context.Parts
            .FirstOrDefaultAsync(p => p.PartNumber == new PartNumber(partNumber), ct);
    }

    /// <summary>
    /// パーツをIDで取得する。
    /// </summary>
    public async Task<DesignComponent?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _context.Parts
            .FirstOrDefaultAsync(p => p.Id == id, ct);
    }

    /// <summary>
    /// パーツを削除する。
    /// </summary>
    public async Task<bool> RemoveAsync(string partNumber, CancellationToken ct = default)
    {
        var part = await _context.Parts
            .FirstOrDefaultAsync(p => p.PartNumber == new PartNumber(partNumber), ct);

        if (part == null)
        {
            return false;
        }

        _context.Parts.Remove(part);
        await _context.SaveChangesAsync(ct);

        return true;
    }

    /// <summary>
    /// パーツを装置にリンクする。
    /// </summary>
    public async Task<AssetComponent> LinkToAssetAsync(
        Guid assetId,
        Guid partId,
        int quantity = 1,
        string? notes = null,
        CancellationToken ct = default)
    {
        // 既存チェック
        var existing = await _context.AssetComponents
            .FirstOrDefaultAsync(ac => ac.AssetId == assetId && ac.PartId == partId, ct);

        if (existing != null)
        {
            existing.UpdateQuantity(quantity);
            existing.Notes = notes;
            await _context.SaveChangesAsync(ct);
            return existing;
        }

        var link = AssetComponent.Create(assetId, partId, quantity, notes);
        _context.AssetComponents.Add(link);
        await _context.SaveChangesAsync(ct);

        return link;
    }

    /// <summary>
    /// 装置に紐づくパーツを取得する。
    /// </summary>
    public async Task<List<(DesignComponent Part, AssetComponent Link)>> GetPartsByAssetAsync(
        Guid assetId,
        CancellationToken ct = default)
    {
        var links = await _context.AssetComponents
            .Include(ac => ac.Part)
            .Where(ac => ac.AssetId == assetId)
            .ToListAsync(ct);

        return links
            .Where(ac => ac.Part != null)
            .Select(ac => (ac.Part!, ac))
            .ToList();
    }
}
