using System.Net;
using Microsoft.Extensions.DependencyInjection;
using PharmaTrack.Application.Common;
using PharmaTrack.Application.Features.Analytics;
using PharmaTrack.Application.Features.Notifications;
using PharmaTrack.Application.Features.References;
using PharmaTrack.Application.Features.Sales;
using PharmaTrack.Application.Features.Stock;
using PharmaTrack.Domain.Enums;
using PharmaTrack.Tests.Support;
using Xunit;

namespace PharmaTrack.Tests;

[Collection(ApiCollection.Name)]
public sealed class DashboardTests(ApiFactory factory)
{
    private readonly TestApi _api = new(factory);

    [Fact]
    public async Task Dashboard_figures_add_up_for_one_dealer()
    {
        var admin = await _api.AdminAsync();
        var provinces = await _api.ProvincesAsync();
        var province = provinces.Single(p => p.NameUz.StartsWith("Samarqand"));
        var dealer = await _api.CreateDealerAsync(province.Id);
        var pharmacy = await _api.CreatePharmacyAsync(province.Id);
        var (product, _) = await _api.StockDealerAsync(dealer, 100, price: 60m);

        await SellAsync(dealer, pharmacy, product, 30, 100m, TestApi.Today());
        await SellAsync(dealer, pharmacy, product, 10, 100m, TestApi.Today().AddDays(-40));

        var dashboard = await TestApi.GetAsync<DashboardDto>(admin, $"/api/v1/analytics/dashboard?dealerId={dealer.Dealer.Id}");

        Assert.Equal(3000m, dashboard.Kpis.SalesAmount.Value);
        Assert.Equal(1000m, dashboard.Kpis.SalesAmount.PreviousValue);
        Assert.Equal(200m, dashboard.Kpis.SalesAmount.ChangePercent);
        Assert.Equal(30m, dashboard.Kpis.SalesUnits.Value);
        Assert.Equal(1200m, dashboard.Kpis.GrossMargin.Value);
        Assert.Equal(40m, dashboard.Kpis.MarginPercent);
        Assert.Equal(60, dashboard.Kpis.StockUnits);
        Assert.Equal(3600m, dashboard.Kpis.StockValue);
        Assert.Equal(60m, dashboard.Kpis.DaysOfStock);
        Assert.Equal(1, dashboard.Kpis.ActiveDealers);
        Assert.Equal(1, dashboard.Kpis.ReportingDealers);

        Assert.Equal(TrendGranularity.Day, dashboard.Granularity);
        Assert.Equal(3000m, dashboard.SalesTrend.Sum(p => p.Amount));
        Assert.Equal(3000m, dashboard.SalesTrend.Last().Amount);

        var region = dashboard.Regions.Single(r => r.RegionId == province.Id);
        Assert.Equal(3000m, region.SalesAmount);
        Assert.Equal(100m, region.SharePercent);
        Assert.Equal(60, region.StockUnits);

        var top = dashboard.TopProducts.Single();
        Assert.Equal(product.Id, top.ProductId);
        Assert.Equal(StockHealth.Healthy, top.Health);

        var row = dashboard.Dealers.Single();
        Assert.Equal(dealer.Dealer.Id, row.DealerId);
        Assert.Equal(ReportingStatus.OnTime, row.Reporting);
        Assert.Equal(0, row.DaysSinceLastSale);

        Assert.Equal(60, dashboard.Distribution.DealerUnits);
        Assert.Equal(0, dashboard.Distribution.CentralUnits);
        Assert.Equal(60, dashboard.ExpiryBuckets.Single(b => b.Key == "90+").Units);
    }

    [Fact]
    public async Task Reversed_sales_do_not_count()
    {
        var admin = await _api.AdminAsync();
        var dealer = await _api.CreateDealerAsync();
        var pharmacy = await _api.CreatePharmacyAsync();
        var (product, _) = await _api.StockDealerAsync(dealer, 10);
        var sale = await SellAsync(dealer, pharmacy, product, 5, 100m, TestApi.Today());
        await TestApi.PostAsync<SaleDto>(dealer.Client, $"/api/v1/sales/{sale.Id}/reverse", new ReverseSaleRequest("Xato"));

        var kpis = await TestApi.GetAsync<DashboardKpis>(admin, $"/api/v1/analytics/summary?dealerId={dealer.Dealer.Id}");
        Assert.Equal(0m, kpis.SalesAmount.Value);
        Assert.Equal(10, kpis.StockUnits);
    }

    [Fact]
    public async Task Pharmacy_returns_reduce_sales_and_refill_stock()
    {
        var admin = await _api.AdminAsync();
        var dealer = await _api.CreateDealerAsync();
        var pharmacy = await _api.CreatePharmacyAsync();
        var (product, batch) = await _api.StockDealerAsync(dealer, 10);
        await SellAsync(dealer, pharmacy, product, 5, 100m, TestApi.Today());

        var request = await TestApi.PostAsync<AdjustmentDto>(dealer.Client, "/api/v1/adjustments",
            new CreateAdjustmentRequest(null, batch.BatchId, AdjustmentType.PharmacyReturn, 2, "Qaytarildi", pharmacy.Id, 100m));
        await TestApi.PostAsync<AdjustmentDto>(admin, $"/api/v1/adjustments/{request.Id}/approve");

        var kpis = await TestApi.GetAsync<DashboardKpis>(admin, $"/api/v1/analytics/summary?dealerId={dealer.Dealer.Id}");
        Assert.Equal(300m, kpis.SalesAmount.Value);
        Assert.Equal(3m, kpis.SalesUnits.Value);
        Assert.Equal(7, kpis.StockUnits);
    }

    [Fact]
    public async Task Sale_counts_in_the_pharmacy_region_not_the_dealer_region()
    {
        var admin = await _api.AdminAsync();
        var provinces = await _api.ProvincesAsync();
        var home = provinces.Single(p => p.NameUz.StartsWith("Buxoro"));
        var away = provinces.Single(p => p.NameUz.StartsWith("Navoiy"));
        var dealer = await _api.CreateDealerAsync(home.Id);
        var pharmacy = await _api.CreatePharmacyAsync(away.Id);
        var (product, _) = await _api.StockDealerAsync(dealer, 10);
        await SellAsync(dealer, pharmacy, product, 4, 50m, TestApi.Today());

        var rows = await TestApi.GetAsync<List<SalesGroupRow>>(admin, $"/api/v1/analytics/sales?groupBy=Province&dealerId={dealer.Dealer.Id}");

        var row = Assert.Single(rows);
        Assert.Equal(away.Id.ToString(), row.Key);
        Assert.Equal(200m, row.Amount);
    }

    [Fact]
    public async Task Province_filter_includes_its_districts()
    {
        var admin = await _api.AdminAsync();
        var province = (await _api.ProvincesAsync()).Single(p => p.NameUz.StartsWith("Jizzax"));
        var district = await TestApi.PostAsync<RegionDto>(admin, "/api/v1/regions",
            new SaveRegionRequest(province.Id, RegionType.District, $"Tuman {Guid.NewGuid():N}", "Район"));
        var dealer = await _api.CreateDealerAsync(district.Id);
        var pharmacy = await _api.CreatePharmacyAsync(district.Id);
        var (product, _) = await _api.StockDealerAsync(dealer, 10);
        await SellAsync(dealer, pharmacy, product, 2, 100m, TestApi.Today());

        var byProvince = await TestApi.GetAsync<DashboardKpis>(admin, $"/api/v1/analytics/summary?regionId={province.Id}&productId={product.Id}");
        var byDistrict = await TestApi.GetAsync<DashboardKpis>(admin, $"/api/v1/analytics/summary?regionId={district.Id}&productId={product.Id}");

        Assert.Equal(200m, byProvince.SalesAmount.Value);
        Assert.Equal(200m, byDistrict.SalesAmount.Value);
        Assert.Equal(8, byProvince.StockUnits);
    }

    [Fact]
    public async Task Dashboard_flags_problems_that_need_attention()
    {
        var admin = await _api.AdminAsync();
        var silent = await _api.CreateDealerAsync();
        var (expiringProduct, _) = await _api.StockDealerAsync(silent, 10, shelfLifeDays: 20);

        var dashboard = await TestApi.GetAsync<DashboardDto>(admin, $"/api/v1/analytics/dashboard?dealerId={silent.Dealer.Id}");

        Assert.Contains(dashboard.Attention, a => a.Type == AttentionType.DealerNotReporting && a.EntityId == silent.Dealer.Id);
        Assert.Equal(AttentionSeverity.Critical, dashboard.Attention[0].Severity);
        Assert.Equal(ReportingStatus.NeverReported, dashboard.Dealers.Single().Reporting);
        Assert.Equal(10, dashboard.ExpiryBuckets.Single(b => b.Key == "0-30").Units);
        Assert.True(dashboard.Kpis.ExpiringValue > 0);
        Assert.Equal(StockHealth.NoSales, dashboard.TopProducts.Single(p => p.ProductId == expiringProduct.Id).Health);
    }

    [Fact]
    public async Task Only_managers_open_the_dashboard()
    {
        var dealer = await _api.CreateDealerAsync();
        var warehouse = await _api.CreateUserAsync(UserRole.Warehouse);
        var executive = await _api.CreateUserAsync(UserRole.Executive);

        Assert.Equal(HttpStatusCode.Forbidden, (await dealer.Client.GetAsync("/api/v1/analytics/dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await warehouse.GetAsync("/api/v1/analytics/dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await executive.GetAsync("/api/v1/analytics/dashboard")).StatusCode);
    }

    [Fact]
    public async Task Executive_can_read_but_not_change_data()
    {
        var executive = await _api.CreateUserAsync(UserRole.Executive);
        Assert.Equal(HttpStatusCode.OK, (await executive.GetAsync("/api/v1/products")).StatusCode);

        var create = await executive.PostAsync("/api/v1/suppliers", System.Net.Http.Json.JsonContent.Create(new SaveSupplierRequest("X", null)));
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
    }

    [Fact]
    public async Task Dealer_reports_contain_only_their_own_data_and_no_cost()
    {
        var dealer = await _api.CreateDealerAsync();
        var other = await _api.CreateDealerAsync();
        var pharmacy = await _api.CreatePharmacyAsync();
        var (mine, _) = await _api.StockDealerAsync(dealer, 10);
        var (theirs, _) = await _api.StockDealerAsync(other, 10);
        await SellAsync(other, pharmacy, theirs, 5, 100m, TestApi.Today());

        var sales = await TestApi.GetAsync<List<SalesGroupRow>>(dealer.Client, $"/api/v1/analytics/sales?groupBy=Product&dealerId={other.Dealer.Id}");
        Assert.Empty(sales);

        var stock = await TestApi.GetAsync<List<StockGroupRow>>(dealer.Client, "/api/v1/analytics/stock?groupBy=Product");
        var row = Assert.Single(stock);
        Assert.Equal(mine.Id.ToString(), row.Key);
        Assert.Null(row.Value);

        var dealers = await TestApi.GetAsync<List<DealerRow>>(dealer.Client, "/api/v1/analytics/dealers");
        Assert.Equal(dealer.Dealer.Id, Assert.Single(dealers).DealerId);
        Assert.Null(dealers[0].StockValue);
    }

    [Fact]
    public async Task Detailed_reports_answer()
    {
        var admin = await _api.AdminAsync();
        var dealer = await _api.CreateDealerAsync();
        var (product, _) = await _api.StockDealerAsync(dealer, 10, shelfLifeDays: 30);

        var expiring = await TestApi.GetAsync<List<ExpiringRow>>(admin, $"/api/v1/analytics/expiring?dealerId={dealer.Dealer.Id}");
        Assert.Equal(30, Assert.Single(expiring).DaysLeft);

        var dead = await TestApi.GetAsync<List<DeadStockRow>>(admin, $"/api/v1/analytics/dead-stock?dealerId={dealer.Dealer.Id}");
        Assert.Equal(product.Id, Assert.Single(dead).ProductId);

        var days = await TestApi.GetAsync<List<ProductStockRow>>(admin, $"/api/v1/analytics/days-of-stock?dealerId={dealer.Dealer.Id}");
        Assert.Equal(StockHealth.NoSales, Assert.Single(days).Health);
    }

    [Fact]
    public async Task Alerts_are_created_once_and_can_be_read()
    {
        var dealer = await _api.CreateDealerAsync();
        await _api.StockDealerAsync(dealer, 10, shelfLifeDays: 20);

        await RunAlertsAsync();
        var count = await TestApi.GetAsync<UnreadCountDto>(dealer.Client, "/api/v1/notifications/unread-count");
        Assert.True(count.Count >= 2, $"Expected expiry and missing-report alerts, got {count.Count}");

        var again = await RunAlertsAsync();
        var afterSecondRun = await TestApi.GetAsync<UnreadCountDto>(dealer.Client, "/api/v1/notifications/unread-count");
        Assert.Equal(count.Count, afterSecondRun.Count);
        Assert.Equal(0, again);

        var page = await TestApi.GetAsync<PagedResult<NotificationDto>>(dealer.Client, "/api/v1/notifications?unreadOnly=true");
        Assert.Contains(page.Items, n => n.Type == NotificationType.ExpiringBatch);

        var readAll = await dealer.Client.PostAsync("/api/v1/notifications/read-all", null);
        Assert.Equal(HttpStatusCode.NoContent, readAll.StatusCode);
        Assert.Equal(0, (await TestApi.GetAsync<UnreadCountDto>(dealer.Client, "/api/v1/notifications/unread-count")).Count);
    }

    private async Task<int> RunAlertsAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AlertService>().RunAsync(CancellationToken.None);
    }

    private static async Task<SaleDto> SellAsync(CreatedDealer dealer, PharmacyDto pharmacy, ProductDto product, int quantity, decimal price, DateOnly date) =>
        await TestApi.ReadAsync<SaleDto>(await TestApi.PostSaleAsync(dealer.Client,
            new CreateSaleRequest(pharmacy.Id, date, null, [new SaleItemRequest(product.Id, quantity, price, null)])));
}
