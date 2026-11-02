namespace PharmaTrack.Application.Features.Analytics;

public sealed class AlertOptions
{
    public const string Section = "Alerts";

    public bool Enabled { get; init; } = true;
    public int ExpiryWarningDays { get; init; } = 90;
    public int LowStockDays { get; init; } = 14;
    public int OverstockDays { get; init; } = 90;
    public int ShipmentStuckDays { get; init; } = 3;
    public int SalesReportGraceDays { get; init; } = 2;
}

public record AnalyticsFilter
{
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    /// <summary>A province includes all of its districts.</summary>
    public Guid? RegionId { get; init; }
    public Guid? DealerId { get; init; }
    public Guid? ProductId { get; init; }
}

public sealed record Period(DateOnly From, DateOnly To)
{
    public int Days => To.DayNumber - From.DayNumber + 1;
    public Period Previous => new(From.AddDays(-Days), From.AddDays(-1));
    public bool Contains(DateOnly date) => date >= From && date <= To;
}

public enum TrendGranularity { Day, Week, Month }
public enum StockHealth { Stockout, Critical, Healthy, Overstock, NoSales }
public enum ReportingStatus { OnTime, Late, NeverReported }
public enum AttentionSeverity { Critical, Warning, Info }
public enum AttentionType { DealerNotReporting, ShipmentStuck, OpenDiscrepancy, ExpiredStock, ProductStockout, PendingAdjustment, PendingPharmacy }
public enum SalesGroupBy { Day, Week, Month, Province, District, Dealer, Pharmacy, Product }
public enum StockGroupBy { Location, Province, Product, Batch }

public sealed record KpiDto(decimal Value, decimal PreviousValue, decimal? ChangePercent);
public sealed record DashboardKpis(
    KpiDto SalesAmount,
    KpiDto SalesUnits,
    KpiDto GrossMargin,
    decimal? MarginPercent,
    int StockUnits,
    decimal StockValue,
    int InTransitUnits,
    decimal InTransitValue,
    decimal? DaysOfStock,
    int ActiveDealers,
    int ReportingDealers,
    decimal ExpiringValue,
    decimal ExpiredValue,
    decimal WriteOffValue,
    int OpenDiscrepancies);
public sealed record TrendPoint(DateOnly Date, decimal Amount, int Units, decimal PreviousAmount);
public sealed record RegionRow(Guid RegionId, string Name, string NameRu, decimal SalesAmount, int SalesUnits, decimal SharePercent, decimal? ChangePercent, int StockUnits, decimal StockValue, decimal? DaysOfStock, int DealerCount, int PharmacyCount);
public sealed record ProductRow(Guid ProductId, string Name, decimal SalesAmount, int SalesUnits, decimal SharePercent, decimal? ChangePercent, int StockUnits, decimal? DaysOfStock, StockHealth Health);
public sealed record DealerRow(Guid DealerId, string Name, Guid RegionId, string RegionName, decimal SalesAmount, int SalesUnits, decimal? ChangePercent, int StockUnits, decimal? StockValue, decimal? DaysOfStock, int ExpiringUnits, DateOnly? LastSaleDate, int? DaysSinceLastSale, ReportingStatus Reporting);
public sealed record StockDistribution(int CentralUnits, decimal CentralValue, int DealerUnits, decimal DealerValue, int InTransitUnits, decimal InTransitValue);
public sealed record ExpiryBucket(string Key, int Units, decimal Value);
public sealed record HealthBucket(StockHealth Health, int Products, int Units, decimal Value);
public sealed record AttentionItem(AttentionSeverity Severity, AttentionType Type, string Message, Guid? EntityId);
public sealed record DashboardDto(
    DateOnly From,
    DateOnly To,
    TrendGranularity Granularity,
    DashboardKpis Kpis,
    IReadOnlyList<TrendPoint> SalesTrend,
    IReadOnlyList<RegionRow> Regions,
    IReadOnlyList<ProductRow> TopProducts,
    IReadOnlyList<DealerRow> Dealers,
    StockDistribution Distribution,
    IReadOnlyList<ExpiryBucket> ExpiryBuckets,
    IReadOnlyList<HealthBucket> StockHealth,
    IReadOnlyList<AttentionItem> Attention,
    DateTimeOffset GeneratedAt);

public sealed record SalesGroupRow(string Key, string Label, int Units, decimal Amount);
public sealed record StockGroupRow(string Key, string Label, int Units, decimal? Value);
public sealed record ExpiringRow(Guid LocationId, string LocationName, Guid ProductId, string ProductName, Guid BatchId, string BatchNumber, DateOnly ExpiryDate, int DaysLeft, int Units, decimal? Value);
public sealed record DeadStockRow(Guid LocationId, string LocationName, Guid ProductId, string ProductName, int Units, decimal? Value, DateOnly? LastSaleDate);
public sealed record ReconciliationRow(Guid LocationId, string LocationName, Guid BatchId, string BatchNumber, int Balance, int JournalTotal);
public sealed record ReconciliationDto(bool IsBalanced, int CheckedBalances, IReadOnlyDictionary<string, int> UnitsByMovementType, IReadOnlyList<ReconciliationRow> Mismatches);
