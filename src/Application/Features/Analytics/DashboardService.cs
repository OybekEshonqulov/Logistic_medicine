using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PharmaTrack.Application.Common;
using PharmaTrack.Domain.Entities;
using PharmaTrack.Domain.Enums;

namespace PharmaTrack.Application.Features.Analytics;

/// <summary>One call that returns everything the executive dashboard shows, consistent as of one moment.</summary>
public sealed class DashboardService(
    IAppDbContext db,
    AnalyticsDataService data,
    AnalyticsReportService reports,
    IOptions<AlertOptions> alertOptions,
    TimeProvider clock)
{
    private const int TopProductCount = 10;
    private const int MaxAttentionItems = 20;

    private readonly AlertOptions _options = alertOptions.Value;

    public async Task<DashboardDto> GetAsync(AnalyticsFilter filter, CancellationToken ct)
    {
        var period = data.ResolvePeriod(filter);
        var regions = await data.RegionsAsync(ct);
        var sales = await data.SalesAsync(filter, new Period(period.Previous.From, period.To), regions, ct);
        var stock = await data.StockAsync(filter, regions, ct);
        var transit = await data.TransitAsync(filter, regions, ct);
        var today = clock.Today();

        var dealers = await reports.DealerRowsAsync(filter, period, sales, stock, ct);
        var products = await ProductRowsAsync(filter, period, sales, stock, ct);
        var granularity = AnalyticsCalc.GranularityFor(period.Days);

        return new DashboardDto(
            period.From,
            period.To,
            granularity,
            await KpisAsync(filter, period, sales, stock, transit, dealers, today, ct),
            AnalyticsCalc.Trend(sales, period, granularity),
            await RegionRowsAsync(filter, period, sales, stock, regions, ct),
            products.Take(TopProductCount).ToList(),
            dealers,
            Distribution(stock, transit),
            AnalyticsCalc.ExpiryBuckets(stock, today),
            HealthBuckets(products, stock),
            await AttentionAsync(filter, dealers, products, stock, today, ct),
            clock.GetUtcNow());
    }

    public async Task<DashboardKpis> KpisOnlyAsync(AnalyticsFilter filter, CancellationToken ct)
    {
        var period = data.ResolvePeriod(filter);
        var regions = await data.RegionsAsync(ct);
        var sales = await data.SalesAsync(filter, new Period(period.Previous.From, period.To), regions, ct);
        var stock = await data.StockAsync(filter, regions, ct);
        var transit = await data.TransitAsync(filter, regions, ct);
        var dealers = await reports.DealerRowsAsync(filter, period, sales, stock, ct);
        return await KpisAsync(filter, period, sales, stock, transit, dealers, clock.Today(), ct);
    }

    private async Task<DashboardKpis> KpisAsync(
        AnalyticsFilter filter, Period period, List<SalesFact> sales, List<StockFact> stock, List<TransitFact> transit,
        IReadOnlyList<DealerRow> dealers, DateOnly today, CancellationToken ct)
    {
        var current = sales.Where(s => period.Contains(s.Date)).ToList();
        var previous = sales.Where(s => period.Previous.Contains(s.Date)).ToList();

        var amount = current.Sum(s => s.Amount);
        var margin = amount - current.Sum(s => s.Cost);
        var previousAmount = previous.Sum(s => s.Amount);
        var previousMargin = previousAmount - previous.Sum(s => s.Cost);
        var stockUnits = stock.Sum(s => s.Units);
        var expiringUntil = today.AddDays(_options.ExpiryWarningDays);

        var discrepancies = db.ShipmentItems.AsNoTracking().Where(i => i.DiscrepancyStatus == DiscrepancyStatus.Open);
        if (filter.DealerId is { } dealerId)
            discrepancies = discrepancies.Where(i => i.Shipment.ToLocation.DealerId == dealerId || i.Shipment.FromLocation.DealerId == dealerId);

        return new DashboardKpis(
            AnalyticsCalc.Kpi(amount, previousAmount),
            AnalyticsCalc.Kpi(current.Sum(s => s.Units), previous.Sum(s => s.Units)),
            AnalyticsCalc.Kpi(margin, previousMargin),
            amount == 0 ? null : Math.Round(margin / amount * 100, 1),
            stockUnits,
            stock.Sum(s => s.Value),
            transit.Sum(t => t.Units),
            transit.Sum(t => t.Value),
            AnalyticsCalc.DaysOfStock(stockUnits, current.Sum(s => s.Units), period.Days),
            dealers.Count,
            dealers.Count(d => d.Reporting == ReportingStatus.OnTime),
            stock.Where(s => s.ExpiryDate >= today && s.ExpiryDate <= expiringUntil).Sum(s => s.Value),
            stock.Where(s => s.ExpiryDate < today).Sum(s => s.Value),
            await data.WriteOffValueAsync(filter, period, ct),
            await discrepancies.CountAsync(ct));
    }

    private async Task<IReadOnlyList<RegionRow>> RegionRowsAsync(
        AnalyticsFilter filter, Period period, List<SalesFact> sales, List<StockFact> stock,
        IReadOnlyDictionary<Guid, RegionInfo> regions, CancellationToken ct)
    {
        var dealers = await db.Dealers.AsNoTracking().Where(d => d.IsActive).Select(d => new { d.Id, d.RegionId }).ToListAsync(ct);
        var pharmacyRegions = await db.Pharmacies.AsNoTracking()
            .Where(p => p.IsActive && p.Status == PharmacyStatus.Active)
            .Select(p => p.RegionId)
            .ToListAsync(ct);

        var dealerProvince = dealers.ToDictionary(d => d.Id, d => regions[d.RegionId].ProvinceId);
        var current = sales.Where(s => period.Contains(s.Date)).ToList();
        var previous = sales.Where(s => period.Previous.Contains(s.Date)).ToList();
        var totalAmount = current.Sum(s => s.Amount);

        var rows = regions.Values.Where(r => r.ParentId is null).Select(province =>
        {
            var regionSales = current.Where(s => regions[s.RegionId].ProvinceId == province.Id).ToList();
            var regionStock = stock.Where(s => s.DealerId is { } id && dealerProvince.GetValueOrDefault(id) == province.Id).ToList();
            var amount = regionSales.Sum(s => s.Amount);
            var units = regionSales.Sum(s => s.Units);
            var previousAmount = previous.Where(s => regions[s.RegionId].ProvinceId == province.Id).Sum(s => s.Amount);
            var stockUnits = regionStock.Sum(s => s.Units);

            return new RegionRow(
                province.Id, province.NameUz, province.NameRu, amount, units,
                AnalyticsCalc.SharePercent(amount, totalAmount),
                AnalyticsCalc.ChangePercent(amount, previousAmount),
                stockUnits, regionStock.Sum(s => s.Value),
                AnalyticsCalc.DaysOfStock(stockUnits, units, period.Days),
                dealerProvince.Values.Count(p => p == province.Id),
                pharmacyRegions.Count(r => regions[r].ProvinceId == province.Id));
        });

        return rows
            .Where(r => filter.RegionId is null || AnalyticsDataService.InRegion(r.RegionId, filter.RegionId, regions) || regions[filter.RegionId.Value].ProvinceId == r.RegionId)
            .OrderByDescending(r => r.SalesAmount)
            .ToList();
    }

    private async Task<List<ProductRow>> ProductRowsAsync(
        AnalyticsFilter filter, Period period, List<SalesFact> sales, List<StockFact> stock, CancellationToken ct)
    {
        var current = sales.Where(s => period.Contains(s.Date)).ToList();
        var ids = current.Select(s => s.ProductId).Concat(stock.Select(s => s.ProductId)).Distinct().ToList();
        var names = await db.Products.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name, ct);
        var totalAmount = current.Sum(s => s.Amount);

        var soldBy = current.GroupBy(s => s.ProductId).ToDictionary(g => g.Key, g => (Amount: g.Sum(s => s.Amount), Units: g.Sum(s => s.Units)));
        var previousBy = sales.Where(s => period.Previous.Contains(s.Date)).GroupBy(s => s.ProductId).ToDictionary(g => g.Key, g => g.Sum(s => s.Amount));
        var stockBy = stock.GroupBy(s => s.ProductId).ToDictionary(g => g.Key, g => g.Sum(s => s.Units));

        return ids.Select(id =>
        {
            var sold = soldBy.GetValueOrDefault(id);
            var stockUnits = stockBy.GetValueOrDefault(id);
            return new ProductRow(
                id, names[id], sold.Amount, sold.Units,
                AnalyticsCalc.SharePercent(sold.Amount, totalAmount),
                AnalyticsCalc.ChangePercent(sold.Amount, previousBy.GetValueOrDefault(id)),
                stockUnits,
                AnalyticsCalc.DaysOfStock(stockUnits, sold.Units, period.Days),
                AnalyticsCalc.HealthOf(stockUnits, sold.Units, period.Days, _options));
        }).OrderByDescending(p => p.SalesAmount).ToList();
    }

    private static StockDistribution Distribution(List<StockFact> stock, List<TransitFact> transit)
    {
        var central = stock.Where(s => s.LocationId == StockLocation.CentralId).ToList();
        var dealers = stock.Where(s => s.LocationId != StockLocation.CentralId).ToList();
        return new StockDistribution(
            central.Sum(s => s.Units), central.Sum(s => s.Value),
            dealers.Sum(s => s.Units), dealers.Sum(s => s.Value),
            transit.Sum(t => t.Units), transit.Sum(t => t.Value));
    }

    private static IReadOnlyList<HealthBucket> HealthBuckets(List<ProductRow> products, List<StockFact> stock)
    {
        var valueBy = stock.GroupBy(s => s.ProductId).ToDictionary(g => g.Key, g => g.Sum(s => s.Value));
        return Enum.GetValues<StockHealth>().Select(health =>
        {
            var rows = products.Where(p => p.Health == health).ToList();
            return new HealthBucket(health, rows.Count, rows.Sum(p => p.StockUnits), rows.Sum(p => valueBy.GetValueOrDefault(p.ProductId)));
        }).ToList();
    }

    private async Task<IReadOnlyList<AttentionItem>> AttentionAsync(
        AnalyticsFilter filter, IReadOnlyList<DealerRow> dealers, List<ProductRow> products, List<StockFact> stock, DateOnly today, CancellationToken ct)
    {
        var items = new List<AttentionItem>();

        items.AddRange(dealers.Where(d => d.Reporting != ReportingStatus.OnTime).Select(d => new AttentionItem(
            AttentionSeverity.Critical, AttentionType.DealerNotReporting,
            d.DaysSinceLastSale is { } days ? $"{d.Name}: {days} kundan beri sotuv kiritmagan" : $"{d.Name}: hali sotuv kiritmagan",
            d.DealerId)));

        items.AddRange(stock.Where(s => s.ExpiryDate < today)
            .GroupBy(s => s.LocationId)
            .Select(g => new AttentionItem(
                AttentionSeverity.Critical, AttentionType.ExpiredStock,
                $"{g.First().LocationName}: {g.Sum(s => s.Units)} dona muddati o'tgan dori", g.Key)));

        items.AddRange(products.Where(p => p.Health == StockHealth.Stockout).Select(p => new AttentionItem(
            AttentionSeverity.Critical, AttentionType.ProductStockout, $"{p.Name}: qoldiq tugagan", p.ProductId)));

        var stuckBefore = clock.GetUtcNow().AddDays(-_options.ShipmentStuckDays);
        var stuck = db.Shipments.AsNoTracking().Where(s => s.Status == ShipmentStatus.InTransit && s.SentAt < stuckBefore);
        var openDiscrepancies = db.Shipments.AsNoTracking().Where(s => s.Items.Any(i => i.DiscrepancyStatus == DiscrepancyStatus.Open));
        if (filter.DealerId is { } dealerId)
        {
            stuck = stuck.Where(s => s.ToLocation.DealerId == dealerId);
            openDiscrepancies = openDiscrepancies.Where(s => s.ToLocation.DealerId == dealerId || s.FromLocation.DealerId == dealerId);
        }

        items.AddRange((await stuck.Select(s => new { s.Id, s.Number, s.ToLocation.Name }).ToListAsync(ct)).Select(s => new AttentionItem(
            AttentionSeverity.Warning, AttentionType.ShipmentStuck, $"#{s.Number} jo'natma {s.Name} tomonidan qabul qilinmagan", s.Id)));

        items.AddRange((await openDiscrepancies.Select(s => new { s.Id, s.Number }).ToListAsync(ct)).Select(s => new AttentionItem(
            AttentionSeverity.Warning, AttentionType.OpenDiscrepancy, $"#{s.Number} jo'natmada hal qilinmagan farq bor", s.Id)));

        var pendingAdjustments = await db.StockAdjustments.AsNoTracking().CountAsync(a => a.Status == AdjustmentStatus.Pending, ct);
        if (pendingAdjustments > 0)
            items.Add(new AttentionItem(AttentionSeverity.Info, AttentionType.PendingAdjustment, $"{pendingAdjustments} ta hisobdan chiqarish so'rovi kutmoqda", null));

        var pendingPharmacies = await db.Pharmacies.AsNoTracking().CountAsync(p => p.Status == PharmacyStatus.Pending, ct);
        if (pendingPharmacies > 0)
            items.Add(new AttentionItem(AttentionSeverity.Info, AttentionType.PendingPharmacy, $"{pendingPharmacies} ta yangi dorixona tasdiq kutmoqda", null));

        return items.OrderBy(i => i.Severity).Take(MaxAttentionItems).ToList();
    }
}
