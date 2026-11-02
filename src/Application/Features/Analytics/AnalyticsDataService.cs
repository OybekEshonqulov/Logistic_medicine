using Microsoft.EntityFrameworkCore;
using PharmaTrack.Application.Common;
using PharmaTrack.Domain.Entities;
using PharmaTrack.Domain.Enums;

namespace PharmaTrack.Application.Features.Analytics;

public sealed record SalesFact(DateOnly Date, Guid DealerId, Guid PharmacyId, Guid RegionId, Guid ProductId, int Units, decimal Amount, decimal Cost);
public sealed record StockFact(Guid LocationId, string LocationName, Guid? DealerId, Guid ProductId, Guid BatchId, string BatchNumber, DateOnly ExpiryDate, int Units, decimal UnitCost)
{
    public decimal Value => Units * UnitCost;
}
public sealed record TransitFact(Guid? DealerId, Guid ProductId, int Units, decimal Value);
public sealed record RegionInfo(Guid Id, Guid? ParentId, string NameUz, string NameRu)
{
    public Guid ProvinceId => ParentId ?? Id;
}

/// <summary>
/// Loads compact facts that the reports then aggregate in memory. Every report therefore counts
/// sales and stock the same way, so two screens can never disagree.
/// </summary>
public sealed class AnalyticsDataService(IAppDbContext db, ICurrentUser currentUser, TimeProvider clock)
{
    private const int DefaultPeriodDays = 30;

    /// <summary>A dealer's filter is always forced to their own data, whatever they asked for.</summary>
    public AnalyticsFilter Scope(AnalyticsFilter filter) =>
        currentUser.IsDealer() ? filter with { DealerId = currentUser.RequireDealerId() } : filter;

    public Period ResolvePeriod(AnalyticsFilter filter)
    {
        var to = filter.To ?? clock.Today();
        var from = filter.From ?? to.AddDays(-(DefaultPeriodDays - 1));
        return from <= to ? new Period(from, to) : new Period(to, from);
    }

    public async Task<IReadOnlyDictionary<Guid, RegionInfo>> RegionsAsync(CancellationToken ct) =>
        await db.Regions.AsNoTracking()
            .Select(r => new RegionInfo(r.Id, r.ParentId, r.NameUz, r.NameRu))
            .ToDictionaryAsync(r => r.Id, ct);

    /// <summary>Net sales: active sales minus approved pharmacy returns, attributed to the pharmacy's region.</summary>
    public async Task<List<SalesFact>> SalesAsync(AnalyticsFilter filter, Period period, IReadOnlyDictionary<Guid, RegionInfo> regions, CancellationToken ct)
    {
        var items = db.SaleItemBatches.AsNoTracking()
            .Where(b => b.SaleItem.Sale.Status == SaleStatus.Active
                && b.SaleItem.Sale.SaleDate >= period.From && b.SaleItem.Sale.SaleDate <= period.To);
        if (filter.DealerId is { } dealerId) items = items.Where(b => b.SaleItem.Sale.DealerId == dealerId);
        if (filter.ProductId is { } productId) items = items.Where(b => b.SaleItem.ProductId == productId);

        var sales = await items
            .GroupBy(b => new
            {
                b.SaleItem.Sale.SaleDate,
                b.SaleItem.Sale.DealerId,
                b.SaleItem.Sale.PharmacyId,
                b.SaleItem.Sale.Pharmacy.RegionId,
                b.SaleItem.ProductId
            })
            .Select(g => new SalesFact(
                g.Key.SaleDate, g.Key.DealerId, g.Key.PharmacyId, g.Key.RegionId, g.Key.ProductId,
                g.Sum(b => b.Quantity),
                g.Sum(b => b.Quantity * b.SaleItem.UnitPrice),
                g.Sum(b => b.Quantity * b.Batch.PurchasePrice)))
            .ToListAsync(ct);

        var returns = await ReturnsAsync(filter, period, ct);
        return sales.Concat(returns).Where(f => InRegion(f.RegionId, filter.RegionId, regions)).ToList();
    }

    public async Task<List<StockFact>> StockAsync(AnalyticsFilter filter, IReadOnlyDictionary<Guid, RegionInfo> regions, CancellationToken ct)
    {
        var balances = db.StockBalances.AsNoTracking().Where(b => b.Quantity > 0);
        if (filter.ProductId is { } productId) balances = balances.Where(b => b.Batch.ProductId == productId);
        if (filter.DealerId is { } dealerId) balances = balances.Where(b => b.Location.DealerId == dealerId);

        var rows = await balances.Select(b => new
        {
            Fact = new StockFact(b.LocationId, b.Location.Name, b.Location.DealerId, b.Batch.ProductId, b.BatchId, b.Batch.BatchNumber, b.Batch.ExpiryDate, b.Quantity, b.Batch.PurchasePrice),
            RegionId = b.Location.Dealer != null ? b.Location.Dealer.RegionId : (Guid?)null
        }).ToListAsync(ct);

        // The central store has no region, so a region filter leaves only dealers' stock.
        return rows
            .Where(r => filter.RegionId is null || (r.RegionId is { } id && InRegion(id, filter.RegionId, regions)))
            .Select(r => r.Fact)
            .ToList();
    }

    public async Task<List<TransitFact>> TransitAsync(AnalyticsFilter filter, IReadOnlyDictionary<Guid, RegionInfo> regions, CancellationToken ct)
    {
        var items = db.ShipmentItems.AsNoTracking().Where(i => i.Shipment.Status == ShipmentStatus.InTransit);
        if (filter.ProductId is { } productId) items = items.Where(i => i.Batch.ProductId == productId);
        if (filter.DealerId is { } dealerId) items = items.Where(i => i.Shipment.ToLocation.DealerId == dealerId);

        var rows = await items.Select(i => new
        {
            i.Shipment.ToLocation.DealerId,
            RegionId = i.Shipment.ToLocation.Dealer != null ? i.Shipment.ToLocation.Dealer.RegionId : (Guid?)null,
            i.Batch.ProductId,
            Units = i.SentQuantity,
            Value = i.SentQuantity * i.Batch.PurchasePrice
        }).ToListAsync(ct);

        return rows
            .Where(r => filter.RegionId is null || (r.RegionId is { } id && InRegion(id, filter.RegionId, regions)))
            .Select(r => new TransitFact(r.DealerId, r.ProductId, r.Units, r.Value))
            .ToList();
    }

    public async Task<Dictionary<Guid, DateOnly>> LastSaleByDealerAsync(CancellationToken ct) =>
        await db.Sales.AsNoTracking()
            .Where(s => s.Status == SaleStatus.Active)
            .GroupBy(s => s.DealerId)
            .Select(g => new { g.Key, Last = g.Max(s => s.SaleDate) })
            .ToDictionaryAsync(x => x.Key, x => x.Last, ct);

    public async Task<decimal> WriteOffValueAsync(AnalyticsFilter filter, Period period, CancellationToken ct)
    {
        var from = new DateTimeOffset(period.From.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var to = new DateTimeOffset(period.To.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        var writeOffs = db.StockAdjustments.AsNoTracking()
            .Where(a => a.Status == AdjustmentStatus.Approved && a.Type != AdjustmentType.PharmacyReturn
                && a.ReviewedAt >= from && a.ReviewedAt < to);
        if (filter.DealerId is { } dealerId) writeOffs = writeOffs.Where(a => a.Location.DealerId == dealerId);
        if (filter.ProductId is { } productId) writeOffs = writeOffs.Where(a => a.Batch.ProductId == productId);

        return await writeOffs.SumAsync(a => a.Quantity * a.Batch.PurchasePrice, ct);
    }

    public static bool InRegion(Guid regionId, Guid? filterRegionId, IReadOnlyDictionary<Guid, RegionInfo> regions)
    {
        if (filterRegionId is null || regionId == filterRegionId) return true;
        return regions.TryGetValue(regionId, out var region) && region.ParentId == filterRegionId;
    }

    private async Task<List<SalesFact>> ReturnsAsync(AnalyticsFilter filter, Period period, CancellationToken ct)
    {
        var returns = db.StockAdjustments.AsNoTracking()
            .Where(a => a.Status == AdjustmentStatus.Approved && a.Type == AdjustmentType.PharmacyReturn
                && a.PharmacyId != null && a.Location.DealerId != null);
        if (filter.DealerId is { } dealerId) returns = returns.Where(a => a.Location.DealerId == dealerId);
        if (filter.ProductId is { } productId) returns = returns.Where(a => a.Batch.ProductId == productId);

        var rows = await returns.Select(a => new
        {
            a.ReviewedAt,
            DealerId = a.Location.DealerId!.Value,
            PharmacyId = a.PharmacyId!.Value,
            a.Pharmacy!.RegionId,
            a.Batch.ProductId,
            a.Quantity,
            Amount = a.Quantity * (a.UnitPrice ?? 0),
            Cost = a.Quantity * a.Batch.PurchasePrice
        }).ToListAsync(ct);

        return rows
            .Select(r => (Date: DateOnly.FromDateTime(r.ReviewedAt!.Value.UtcDateTime), Row: r))
            .Where(x => period.Contains(x.Date))
            .Select(x => new SalesFact(x.Date, x.Row.DealerId, x.Row.PharmacyId, x.Row.RegionId, x.Row.ProductId, -x.Row.Quantity, -x.Row.Amount, -x.Row.Cost))
            .ToList();
    }
}
