namespace PharmaTrack.Application.Features.Analytics;

public static class AnalyticsCalc
{
    public static decimal? ChangePercent(decimal current, decimal previous) =>
        previous == 0 ? null : Math.Round((current - previous) / Math.Abs(previous) * 100, 1);

    public static decimal SharePercent(decimal part, decimal total) =>
        total == 0 ? 0 : Math.Round(part / total * 100, 1);

    /// <summary>How long the stock lasts at the period's average daily sales; null when nothing was sold.</summary>
    public static decimal? DaysOfStock(int stockUnits, int soldUnits, int periodDays) =>
        soldUnits <= 0 ? null : Math.Round(stockUnits / (soldUnits / (decimal)periodDays), 1);

    public static StockHealth HealthOf(int stockUnits, int soldUnits, int periodDays, AlertOptions options)
    {
        if (stockUnits == 0) return StockHealth.Stockout;
        if (soldUnits <= 0) return StockHealth.NoSales;

        var days = DaysOfStock(stockUnits, soldUnits, periodDays);
        if (days < options.LowStockDays) return StockHealth.Critical;
        return days > options.OverstockDays ? StockHealth.Overstock : StockHealth.Healthy;
    }

    public static KpiDto Kpi(decimal current, decimal previous) => new(current, previous, ChangePercent(current, previous));

    public static TrendGranularity GranularityFor(int days) => days switch
    {
        <= 45 => TrendGranularity.Day,
        <= 180 => TrendGranularity.Week,
        _ => TrendGranularity.Month
    };

    public static DateOnly BucketStart(DateOnly date, TrendGranularity granularity) => granularity switch
    {
        TrendGranularity.Week => date.AddDays(-(((int)date.DayOfWeek + 6) % 7)),
        TrendGranularity.Month => new DateOnly(date.Year, date.Month, 1),
        _ => date
    };

    public static IReadOnlyList<TrendPoint> Trend(IReadOnlyList<SalesFact> sales, Period period, TrendGranularity granularity)
    {
        var offset = period.Days;
        var current = sales.Where(s => period.Contains(s.Date))
            .GroupBy(s => BucketStart(s.Date, granularity))
            .ToDictionary(g => g.Key, g => (Amount: g.Sum(s => s.Amount), Units: g.Sum(s => s.Units)));
        var previous = sales.Where(s => period.Previous.Contains(s.Date))
            .GroupBy(s => BucketStart(s.Date.AddDays(offset), granularity))
            .ToDictionary(g => g.Key, g => g.Sum(s => s.Amount));

        var points = new List<TrendPoint>();
        for (var day = BucketStart(period.From, granularity); day <= period.To; day = NextBucket(day, granularity))
        {
            var value = current.GetValueOrDefault(day);
            points.Add(new TrendPoint(day, value.Amount, value.Units, previous.GetValueOrDefault(day)));
        }

        return points;
    }

    public static IReadOnlyList<ExpiryBucket> ExpiryBuckets(IReadOnlyList<StockFact> stock, DateOnly today)
    {
        (string Key, int From, int To)[] ranges =
        [
            ("expired", int.MinValue, -1),
            ("0-30", 0, 30),
            ("31-60", 31, 60),
            ("61-90", 61, 90),
            ("90+", 91, int.MaxValue)
        ];

        return ranges.Select(range =>
        {
            var facts = stock.Where(s =>
            {
                var left = s.ExpiryDate.DayNumber - today.DayNumber;
                return left >= range.From && left <= range.To;
            }).ToList();
            return new ExpiryBucket(range.Key, facts.Sum(f => f.Units), facts.Sum(f => f.Value));
        }).ToList();
    }

    private static DateOnly NextBucket(DateOnly day, TrendGranularity granularity) => granularity switch
    {
        TrendGranularity.Week => day.AddDays(7),
        TrendGranularity.Month => day.AddMonths(1),
        _ => day.AddDays(1)
    };
}
