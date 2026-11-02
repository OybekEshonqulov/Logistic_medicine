using PharmaTrack.Application.Features.Analytics;
using PharmaTrack.Domain.Common;
using PharmaTrack.Domain.Entities;
using PharmaTrack.Domain.Enums;
using Xunit;

namespace PharmaTrack.Tests;

public sealed class AnalyticsCalcTests
{
    private static readonly AlertOptions Options = new() { LowStockDays = 14, OverstockDays = 90 };

    [Fact]
    public void DaysOfStock_uses_average_daily_sales() =>
        Assert.Equal(30m, AnalyticsCalc.DaysOfStock(stockUnits: 100, soldUnits: 100, periodDays: 30));

    [Fact]
    public void DaysOfStock_is_null_without_sales() =>
        Assert.Null(AnalyticsCalc.DaysOfStock(100, 0, 30));

    [Fact]
    public void ChangePercent_is_null_without_a_baseline() =>
        Assert.Null(AnalyticsCalc.ChangePercent(100, 0));

    [Theory]
    [InlineData(150, 100, 50)]
    [InlineData(50, 100, -50)]
    public void ChangePercent_is_signed(int current, int previous, int expected) =>
        Assert.Equal(expected, AnalyticsCalc.ChangePercent(current, previous));

    [Theory]
    [InlineData(0, 10, StockHealth.Stockout)]
    [InlineData(10, 0, StockHealth.NoSales)]
    [InlineData(10, 30, StockHealth.Critical)]
    [InlineData(30, 30, StockHealth.Healthy)]
    [InlineData(100, 30, StockHealth.Overstock)]
    public void HealthOf_classifies_by_days_of_stock(int stock, int sold, StockHealth expected) =>
        Assert.Equal(expected, AnalyticsCalc.HealthOf(stock, sold, 30, Options));

    [Fact]
    public void Trend_fills_days_without_sales_and_aligns_the_previous_period()
    {
        var period = new Period(new DateOnly(2026, 1, 4), new DateOnly(2026, 1, 6));
        var sales = new[]
        {
            Fact(new DateOnly(2026, 1, 6), 100),
            Fact(new DateOnly(2026, 1, 3), 40)
        };

        var trend = AnalyticsCalc.Trend(sales, period, TrendGranularity.Day);

        Assert.Equal(3, trend.Count);
        Assert.Equal(0, trend[0].Amount);
        Assert.Equal(100, trend[2].Amount);
        Assert.Equal(40, trend[2].PreviousAmount);
    }

    [Fact]
    public void ExpiryBuckets_put_each_batch_in_exactly_one_bucket()
    {
        var today = new DateOnly(2026, 1, 1);
        var stock = new[] { -1, 0, 30, 31, 90, 91 }.Select(days => Stock(today.AddDays(days))).ToList();

        var buckets = AnalyticsCalc.ExpiryBuckets(stock, today).ToDictionary(b => b.Key, b => b.Units);

        Assert.Equal(1, buckets["expired"]);
        Assert.Equal(2, buckets["0-30"]);
        Assert.Equal(1, buckets["31-60"]);
        Assert.Equal(1, buckets["61-90"]);
        Assert.Equal(1, buckets["90+"]);
    }

    private static SalesFact Fact(DateOnly date, decimal amount) =>
        new(date, Guid.Empty, Guid.Empty, Guid.Empty, Guid.Empty, 1, amount, 0);

    private static StockFact Stock(DateOnly expiry) =>
        new(Guid.Empty, "x", null, Guid.Empty, Guid.Empty, "b", expiry, 1, 1);
}

public sealed class ShipmentStateTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;

    [Fact]
    public void Follows_the_agreed_order()
    {
        var shipment = new Shipment();
        shipment.Approve(Now);
        shipment.MarkSent(Now);
        shipment.MarkReceived(Now);
        Assert.Equal(ShipmentStatus.Received, shipment.Status);
    }

    [Fact]
    public void Cannot_be_sent_before_approval() =>
        AssertCode(ErrorCodes.InvalidShipmentStatus, () => new Shipment().MarkSent(Now));

    [Fact]
    public void Cannot_be_cancelled_once_in_transit()
    {
        var shipment = new Shipment();
        shipment.Approve(Now);
        shipment.MarkSent(Now);
        AssertCode(ErrorCodes.InvalidShipmentStatus, shipment.Cancel);
    }

    [Fact]
    public void Item_difference_is_open_until_resolved()
    {
        var item = new ShipmentItem { SentQuantity = 10 };
        item.Receive(8, "shikastlangan");
        Assert.Equal(2, item.Difference);
        Assert.Equal(DiscrepancyStatus.Open, item.DiscrepancyStatus);

        item.Resolve(DiscrepancyResolution.AcceptAsIs, null);
        Assert.Equal(DiscrepancyStatus.Resolved, item.DiscrepancyStatus);
        AssertCode(ErrorCodes.DiscrepancyNotOpen, () => item.Resolve(DiscrepancyResolution.AcceptAsIs, null));
    }

    private static void AssertCode(string code, Action action) =>
        Assert.Equal(code, Assert.Throws<DomainException>(action).Code);
}
