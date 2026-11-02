using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PharmaTrack.Application.Common;
using PharmaTrack.Domain.Enums;

namespace PharmaTrack.Application.Features.Analytics;

public sealed class AnalyticsReportService(
    IAppDbContext db,
    AnalyticsDataService data,
    ICurrentUser currentUser,
    IOptions<AlertOptions> alertOptions,
    TimeProvider clock)
{
    private readonly AlertOptions _options = alertOptions.Value;

    public async Task<IReadOnlyList<SalesGroupRow>> SalesAsync(AnalyticsFilter filter, SalesGroupBy groupBy, CancellationToken ct)
    {
        filter = data.Scope(filter);
        var period = data.ResolvePeriod(filter);
        var regions = await data.RegionsAsync(ct);
        var sales = await data.SalesAsync(filter, period, regions, ct);

        var labels = await SalesLabelsAsync(groupBy, sales, regions, ct);
        return sales
            .GroupBy(s => SalesKey(s, groupBy, regions))
            .Select(g => new SalesGroupRow(g.Key, labels.GetValueOrDefault(g.Key, g.Key), g.Sum(s => s.Units), g.Sum(s => s.Amount)))
            .OrderBy(r => groupBy is SalesGroupBy.Day or SalesGroupBy.Week or SalesGroupBy.Month ? r.Key : string.Empty)
            .ThenByDescending(r => r.Amount)
            .ToList();
    }

    public async Task<IReadOnlyList<StockGroupRow>> StockAsync(AnalyticsFilter filter, StockGroupBy groupBy, CancellationToken ct)
    {
        filter = data.Scope(filter);
        var regions = await data.RegionsAsync(ct);
        var stock = await data.StockAsync(filter, regions, ct);
        var showValue = !currentUser.IsDealer();

        var productNames = await db.Products.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Name, ct);
        var dealerRegions = await db.Dealers.AsNoTracking().ToDictionaryAsync(d => d.Id, d => d.RegionId, ct);

        (string Key, string Label) Group(StockFact s)
        {
            switch (groupBy)
            {
                case StockGroupBy.Product:
                    return (s.ProductId.ToString(), productNames[s.ProductId]);
                case StockGroupBy.Batch:
                    return (s.BatchId.ToString(), $"{productNames[s.ProductId]} / {s.BatchNumber}");
                case StockGroupBy.Province when s.DealerId is { } dealerId:
                    var province = regions[regions[dealerRegions[dealerId]].ProvinceId];
                    return (province.Id.ToString(), province.NameUz);
                case StockGroupBy.Province:
                    return (s.LocationId.ToString(), s.LocationName);
                default:
                    return (s.LocationId.ToString(), s.LocationName);
            }
        }

        return stock
            .GroupBy(Group)
            .Select(g => new StockGroupRow(g.Key.Key, g.Key.Label, g.Sum(s => s.Units), showValue ? g.Sum(s => s.Value) : null))
            .OrderByDescending(r => r.Units)
            .ToList();
    }

    public async Task<IReadOnlyList<ExpiringRow>> ExpiringAsync(AnalyticsFilter filter, int? withinDays, CancellationToken ct)
    {
        filter = data.Scope(filter);
        var regions = await data.RegionsAsync(ct);
        var stock = await data.StockAsync(filter, regions, ct);
        var today = clock.Today();
        var until = today.AddDays(withinDays ?? _options.ExpiryWarningDays);
        var names = await ProductNamesAsync(stock.Select(s => s.ProductId), ct);
        var showValue = !currentUser.IsDealer();

        return stock.Where(s => s.ExpiryDate <= until)
            .OrderBy(s => s.ExpiryDate)
            .Select(s => new ExpiringRow(s.LocationId, s.LocationName, s.ProductId, names[s.ProductId], s.BatchId, s.BatchNumber, s.ExpiryDate, s.ExpiryDate.DayNumber - today.DayNumber, s.Units, showValue ? s.Value : null))
            .ToList();
    }

    public async Task<IReadOnlyList<ProductStockRow>> DaysOfStockAsync(AnalyticsFilter filter, CancellationToken ct)
    {
        filter = data.Scope(filter);
        var period = data.ResolvePeriod(filter);
        var regions = await data.RegionsAsync(ct);
        var sales = await data.SalesAsync(filter, period, regions, ct);
        var stock = await data.StockAsync(filter, regions, ct);

        var soldBy = sales.GroupBy(s => s.ProductId).ToDictionary(g => g.Key, g => g.Sum(s => s.Units));
        var stockBy = stock.GroupBy(s => s.ProductId).ToDictionary(g => g.Key, g => g.Sum(s => s.Units));
        var ids = soldBy.Keys.Union(stockBy.Keys).ToList();
        var names = await ProductNamesAsync(ids, ct);

        return ids.Select(id =>
        {
            var sold = soldBy.GetValueOrDefault(id);
            var units = stockBy.GetValueOrDefault(id);
            return new ProductStockRow(id, names[id], units, sold, AnalyticsCalc.DaysOfStock(units, sold, period.Days), AnalyticsCalc.HealthOf(units, sold, period.Days, _options));
        }).OrderBy(r => r.DaysOfStock ?? decimal.MaxValue).ToList();
    }

    /// <summary>Stock of products a location has not sold for the given number of days.</summary>
    public async Task<IReadOnlyList<DeadStockRow>> DeadStockAsync(AnalyticsFilter filter, int days, CancellationToken ct)
    {
        filter = data.Scope(filter);
        var regions = await data.RegionsAsync(ct);
        var stock = await data.StockAsync(filter, regions, ct);
        var cutoff = clock.Today().AddDays(-days);
        var showValue = !currentUser.IsDealer();

        var lastSales = await db.SaleItems.AsNoTracking()
            .Where(i => i.Sale.Status == SaleStatus.Active)
            .GroupBy(i => new { i.Sale.DealerId, i.ProductId })
            .Select(g => new { g.Key.DealerId, g.Key.ProductId, Last = g.Max(i => i.Sale.SaleDate) })
            .ToDictionaryAsync(x => (x.DealerId, x.ProductId), x => x.Last, ct);
        var names = await ProductNamesAsync(stock.Select(s => s.ProductId), ct);

        return stock.Where(s => s.DealerId is not null)
            .GroupBy(s => new { s.LocationId, s.LocationName, DealerId = s.DealerId!.Value, s.ProductId })
            .Select(g => new
            {
                Group = g,
                Last = lastSales.TryGetValue((g.Key.DealerId, g.Key.ProductId), out var last) ? last : (DateOnly?)null
            })
            .Where(x => x.Last is null || x.Last < cutoff)
            .Select(x => new DeadStockRow(x.Group.Key.LocationId, x.Group.Key.LocationName, x.Group.Key.ProductId, names[x.Group.Key.ProductId], x.Group.Sum(s => s.Units), showValue ? x.Group.Sum(s => s.Value) : null, x.Last))
            .OrderByDescending(r => r.Units)
            .ToList();
    }

    public async Task<IReadOnlyList<DealerRow>> DealersAsync(AnalyticsFilter filter, CancellationToken ct)
    {
        filter = data.Scope(filter);
        var period = data.ResolvePeriod(filter);
        var regions = await data.RegionsAsync(ct);
        var sales = await data.SalesAsync(filter, new Period(period.Previous.From, period.To), regions, ct);
        var stock = await data.StockAsync(filter, regions, ct);
        return await DealerRowsAsync(filter, period, sales, stock, ct);
    }

    public async Task<IReadOnlyList<DealerRow>> DealerRowsAsync(
        AnalyticsFilter filter, Period period, List<SalesFact> sales, List<StockFact> stock, CancellationToken ct)
    {
        var regions = await data.RegionsAsync(ct);
        var lastSales = await data.LastSaleByDealerAsync(ct);
        var today = clock.Today();
        var expiringUntil = today.AddDays(_options.ExpiryWarningDays);
        var showValue = !currentUser.IsDealer();

        var dealers = db.Dealers.AsNoTracking().Where(d => d.IsActive);
        if (filter.DealerId is { } dealerId) dealers = dealers.Where(d => d.Id == dealerId);
        var rows = await dealers.Select(d => new { d.Id, d.Name, d.RegionId }).ToListAsync(ct);

        return rows
            .Where(d => AnalyticsDataService.InRegion(d.RegionId, filter.RegionId, regions))
            .Select(d =>
            {
                var current = sales.Where(s => s.DealerId == d.Id && period.Contains(s.Date)).ToList();
                var previousAmount = sales.Where(s => s.DealerId == d.Id && period.Previous.Contains(s.Date)).Sum(s => s.Amount);
                var dealerStock = stock.Where(s => s.DealerId == d.Id).ToList();
                var amount = current.Sum(s => s.Amount);
                var units = current.Sum(s => s.Units);
                var stockUnits = dealerStock.Sum(s => s.Units);
                var hasSale = lastSales.TryGetValue(d.Id, out var last);
                int? daysSince = hasSale ? today.DayNumber - last.DayNumber : null;

                return new DealerRow(
                    d.Id, d.Name, d.RegionId, regions[d.RegionId].NameUz, amount, units,
                    AnalyticsCalc.ChangePercent(amount, previousAmount),
                    stockUnits,
                    showValue ? dealerStock.Sum(s => s.Value) : null,
                    AnalyticsCalc.DaysOfStock(stockUnits, units, period.Days),
                    dealerStock.Where(s => s.ExpiryDate <= expiringUntil).Sum(s => s.Units),
                    hasSale ? last : null,
                    daysSince,
                    ReportingOf(daysSince));
            })
            .OrderByDescending(d => d.SalesAmount)
            .ToList();
    }

    public async Task<ReconciliationDto> ReconcileAsync(CancellationToken ct)
    {
        var journal = await db.StockMovements.AsNoTracking()
            .GroupBy(m => new { m.LocationId, m.BatchId })
            .Select(g => new { g.Key.LocationId, g.Key.BatchId, Total = g.Sum(m => m.QuantityDelta) })
            .ToDictionaryAsync(x => (x.LocationId, x.BatchId), x => x.Total, ct);

        var balances = await db.StockBalances.AsNoTracking()
            .Select(b => new { b.LocationId, LocationName = b.Location.Name, b.BatchId, b.Batch.BatchNumber, b.Quantity })
            .ToListAsync(ct);

        var byType = await db.StockMovements.AsNoTracking()
            .GroupBy(m => m.Type)
            .Select(g => new { g.Key, Units = g.Sum(m => m.QuantityDelta) })
            .ToDictionaryAsync(x => x.Key.ToString(), x => x.Units, ct);

        var mismatches = balances
            .Select(b => new ReconciliationRow(b.LocationId, b.LocationName, b.BatchId, b.BatchNumber, b.Quantity, journal.GetValueOrDefault((b.LocationId, b.BatchId))))
            .Where(r => r.Balance != r.JournalTotal)
            .ToList();

        return new ReconciliationDto(mismatches.Count == 0, balances.Count, byType, mismatches);
    }

    private ReportingStatus ReportingOf(int? daysSinceLastSale) => daysSinceLastSale switch
    {
        null => ReportingStatus.NeverReported,
        var days when days > _options.SalesReportGraceDays => ReportingStatus.Late,
        _ => ReportingStatus.OnTime
    };

    private static string SalesKey(SalesFact s, SalesGroupBy groupBy, IReadOnlyDictionary<Guid, RegionInfo> regions) => groupBy switch
    {
        SalesGroupBy.Day => s.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        SalesGroupBy.Week => AnalyticsCalc.BucketStart(s.Date, TrendGranularity.Week).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        SalesGroupBy.Month => s.Date.ToString("yyyy-MM", CultureInfo.InvariantCulture),
        SalesGroupBy.Province => regions[s.RegionId].ProvinceId.ToString(),
        SalesGroupBy.District => s.RegionId.ToString(),
        SalesGroupBy.Dealer => s.DealerId.ToString(),
        SalesGroupBy.Pharmacy => s.PharmacyId.ToString(),
        _ => s.ProductId.ToString()
    };

    private async Task<IReadOnlyDictionary<string, string>> SalesLabelsAsync(
        SalesGroupBy groupBy, List<SalesFact> sales, IReadOnlyDictionary<Guid, RegionInfo> regions, CancellationToken ct)
    {
        switch (groupBy)
        {
            case SalesGroupBy.Province or SalesGroupBy.District:
                return regions.ToDictionary(r => r.Key.ToString(), r => r.Value.NameUz);
            case SalesGroupBy.Dealer:
                var dealerIds = sales.Select(s => s.DealerId).Distinct().ToList();
                return await db.Dealers.AsNoTracking().Where(d => dealerIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id.ToString(), d => d.Name, ct);
            case SalesGroupBy.Pharmacy:
                var pharmacyIds = sales.Select(s => s.PharmacyId).Distinct().ToList();
                return await db.Pharmacies.AsNoTracking().Where(p => pharmacyIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id.ToString(), p => p.Name, ct);
            case SalesGroupBy.Product:
                var productIds = sales.Select(s => s.ProductId).Distinct().ToList();
                return await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id.ToString(), p => p.Name, ct);
            default:
                return new Dictionary<string, string>();
        }
    }

    private async Task<Dictionary<Guid, string>> ProductNamesAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var distinct = ids.Distinct().ToList();
        return await db.Products.AsNoTracking().Where(p => distinct.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name, ct);
    }
}

public sealed record ProductStockRow(Guid ProductId, string Name, int StockUnits, int SoldUnits, decimal? DaysOfStock, StockHealth Health);
