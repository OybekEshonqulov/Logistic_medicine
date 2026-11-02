using Microsoft.EntityFrameworkCore;
using PharmaTrack.Application.Common;
using PharmaTrack.Application.Features.References;
using PharmaTrack.Domain.Enums;

namespace PharmaTrack.Application.Features.Sync;

public sealed record SyncStockDto(Guid ProductId, int Quantity);
public sealed record SyncResponse(DateTimeOffset ServerTime, IReadOnlyList<ProductDto> Products, IReadOnlyList<PharmacyDto> Pharmacies, IReadOnlyList<RegionDto> Regions, IReadOnlyList<DistrictDto> Districts, IReadOnlyList<SyncStockDto> MyStock);

/// <summary>Gives the mobile app what it needs to work offline. Keep ServerTime and send it back as updatedSince next time.</summary>
public sealed class SyncService(IAppDbContext db, ICurrentUser currentUser, TimeProvider clock)
{
    public async Task<SyncResponse> GetAsync(DateTimeOffset? updatedSince, CancellationToken ct)
    {
        var serverTime = clock.GetUtcNow();
        var since = updatedSince ?? DateTimeOffset.MinValue;
        var dealerId = currentUser.DealerId;

        var products = await db.Products.AsNoTracking().Where(p => p.UpdatedAt > since)
            .Select(p => new ProductDto(p.Id, p.Name, p.Form, p.Dosage, p.ManufacturerId, p.Manufacturer.Name, p.Gtin, p.IsActive))
            .ToListAsync(ct);

        var pharmacies = await db.Pharmacies.AsNoTracking()
            .Where(p => p.UpdatedAt > since && (p.Status == PharmacyStatus.Active || p.RequestedByDealerId == dealerId))
            .Select(p => new PharmacyDto(p.Id, p.Name, p.RegionId, p.Region.NameUz, p.DistrictId, p.District != null ? p.District.NameUz : null, p.Address, p.Phone, p.Status, p.IsActive))
            .ToListAsync(ct);

        var regions = await db.Regions.AsNoTracking().Where(r => r.UpdatedAt > since)
            .Select(r => new RegionDto(r.Id, r.Code, r.NameUz, r.NameRu, r.IsActive))
            .ToListAsync(ct);

        var districts = await db.Districts.AsNoTracking().Where(d => d.UpdatedAt > since)
            .Select(d => new DistrictDto(d.Id, d.RegionId, d.Code, d.Type, d.NameUz, d.NameRu, d.IsActive))
            .ToListAsync(ct);

        var stock = await db.StockBalances.AsNoTracking()
            .Where(b => b.Location.DealerId == dealerId && b.Quantity > 0)
            .GroupBy(b => b.Batch.ProductId)
            .Select(g => new SyncStockDto(g.Key, g.Sum(b => b.Quantity)))
            .ToListAsync(ct);

        return new SyncResponse(serverTime, products, pharmacies, regions, districts, stock);
    }
}
