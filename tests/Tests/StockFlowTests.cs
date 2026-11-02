using System.Net;
using System.Net.Http.Json;
using PharmaTrack.Application.Common;
using PharmaTrack.Application.Features.Analytics;
using PharmaTrack.Application.Features.References;
using PharmaTrack.Application.Features.Sales;
using PharmaTrack.Application.Features.Stock;
using PharmaTrack.Domain.Common;
using PharmaTrack.Domain.Entities;
using PharmaTrack.Domain.Enums;
using PharmaTrack.Tests.Support;
using Xunit;

namespace PharmaTrack.Tests;

[Collection(ApiCollection.Name)]
public sealed class StockFlowTests(ApiFactory factory)
{
    private readonly TestApi _api = new(factory);

    [Fact]
    public async Task Goods_move_from_centre_through_transit_to_dealer()
    {
        var admin = await _api.AdminAsync();
        var dealer = await _api.CreateDealerAsync();
        var product = await _api.CreateProductAsync();
        var batch = await _api.ReceiveAsync(product.Id, "A1", TestApi.Today().AddDays(300), 100, 10m);
        Assert.Equal(100, await TestApi.StockOfAsync(admin, product.Id));

        var shipment = await _api.ShipAsync(dealer.Dealer.Id, batch.BatchId, 40);
        Assert.Equal(ShipmentStatus.InTransit, shipment.Status);
        Assert.Equal(60, await TestApi.StockOfAsync(admin, product.Id));
        Assert.Equal(0, await TestApi.StockOfAsync(dealer.Client, product.Id));

        await TestApi.PostAsync<ShipmentDto>(dealer.Client, $"/api/v1/shipments/{shipment.Id}/receive",
            new ReceiveShipmentRequest([new ReceiveItemRequest(shipment.Items[0].Id, 40, null)]));
        Assert.Equal(40, await TestApi.StockOfAsync(dealer.Client, product.Id));
        Assert.Equal(100, await TestApi.StockOfAsync(admin, product.Id));
    }

    [Fact]
    public async Task Cannot_ship_more_than_is_on_hand()
    {
        var admin = await _api.AdminAsync();
        var dealer = await _api.CreateDealerAsync();
        var product = await _api.CreateProductAsync();
        var batch = await _api.ReceiveAsync(product.Id, "A1", TestApi.Today().AddDays(300), 5, 10m);

        var shipment = await TestApi.PostAsync<ShipmentDto>(admin, "/api/v1/shipments", new CreateShipmentRequest(dealer.Dealer.Id, null, [new ShipmentItemRequest(batch.BatchId, 6)]));
        await TestApi.PostAsync<ShipmentDto>(admin, $"/api/v1/shipments/{shipment.Id}/approve");

        var send = await admin.PostAsync($"/api/v1/shipments/{shipment.Id}/send", null);
        await TestApi.AssertErrorAsync(send, HttpStatusCode.Conflict, ErrorCodes.InsufficientStock);
        Assert.Equal(5, await TestApi.StockOfAsync(admin, product.Id));

        var reloaded = await TestApi.GetAsync<ShipmentDto>(admin, $"/api/v1/shipments/{shipment.Id}");
        Assert.Equal(ShipmentStatus.Approved, reloaded.Status);
    }

    [Fact]
    public async Task Batch_cannot_change_its_expiry_date()
    {
        var product = await _api.CreateProductAsync();
        await _api.ReceiveAsync(product.Id, "A1", TestApi.Today().AddDays(300), 5, 10m);

        var admin = await _api.AdminAsync();
        var supplier = await TestApi.PostAsync<SupplierDto>(admin, "/api/v1/suppliers", new SaveSupplierRequest("Yetkazuvchi", null));
        var response = await admin.PostAsJsonAsync("/api/v1/receipts", new CreateReceiptRequest("D2", supplier.Id, TestApi.Today(), null,
            [new ReceiptItemRequest(product.Id, "A1", TestApi.Today().AddDays(400), 5, 10m)]), TestApi.Json);

        await TestApi.AssertErrorAsync(response, HttpStatusCode.Conflict, ErrorCodes.BatchExpiryMismatch);
        Assert.Equal(5, await TestApi.StockOfAsync(admin, product.Id));
    }

    [Fact]
    public async Task Expired_batch_cannot_go_to_a_dealer()
    {
        var admin = await _api.AdminAsync();
        var dealer = await _api.CreateDealerAsync();
        var product = await _api.CreateProductAsync();
        var batch = await _api.ReceiveAsync(product.Id, "OLD", TestApi.Today().AddDays(-1), 5, 10m);

        var response = await admin.PostAsJsonAsync("/api/v1/shipments", new CreateShipmentRequest(dealer.Dealer.Id, null, [new ShipmentItemRequest(batch.BatchId, 1)]), TestApi.Json);
        await TestApi.AssertErrorAsync(response, HttpStatusCode.BadRequest, ErrorCodes.BatchExpired);
    }

    [Fact]
    public async Task Short_delivery_opens_a_discrepancy_that_admin_resolves()
    {
        var admin = await _api.AdminAsync();
        var dealer = await _api.CreateDealerAsync();
        var product = await _api.CreateProductAsync();
        var batch = await _api.ReceiveAsync(product.Id, "A1", TestApi.Today().AddDays(300), 10, 10m);
        var shipment = await _api.ShipAsync(dealer.Dealer.Id, batch.BatchId, 10);
        var itemId = shipment.Items[0].Id;

        var received = await TestApi.PostAsync<ShipmentDto>(dealer.Client, $"/api/v1/shipments/{shipment.Id}/receive",
            new ReceiveShipmentRequest([new ReceiveItemRequest(itemId, 7, "3 tasi yetib kelmadi")]));
        Assert.Equal(DiscrepancyStatus.Open, received.Items[0].DiscrepancyStatus);
        Assert.Equal(7, await TestApi.StockOfAsync(dealer.Client, product.Id));
        Assert.Equal(0, await TestApi.StockOfAsync(admin, product.Id, Guid.Empty));

        var dealerTry = await dealer.Client.PostAsJsonAsync($"/api/v1/shipments/{shipment.Id}/items/{itemId}/resolve-discrepancy",
            new ResolveDiscrepancyRequest(DiscrepancyResolution.CorrectSender, null), TestApi.Json);
        Assert.Equal(HttpStatusCode.Forbidden, dealerTry.StatusCode);

        var resolved = await TestApi.PostAsync<ShipmentDto>(admin, $"/api/v1/shipments/{shipment.Id}/items/{itemId}/resolve-discrepancy",
            new ResolveDiscrepancyRequest(DiscrepancyResolution.CorrectSender, "Omborda qolib ketgan"));
        Assert.Equal(DiscrepancyStatus.Resolved, resolved.Items[0].DiscrepancyStatus);
        Assert.Equal(10, await TestApi.StockOfAsync(admin, product.Id));
    }

    [Fact]
    public async Task Dealer_can_return_goods_to_the_centre()
    {
        var admin = await _api.AdminAsync();
        var dealer = await _api.CreateDealerAsync();
        var (product, batch) = await _api.StockDealerAsync(dealer, 10);

        var back = await TestApi.PostAsync<ShipmentDto>(dealer.Client, "/api/v1/shipments", new CreateShipmentRequest(null, "Qaytarish", [new ShipmentItemRequest(batch.BatchId, 4)]));
        await TestApi.PostAsync<ShipmentDto>(admin, $"/api/v1/shipments/{back.Id}/approve");
        await TestApi.PostAsync<ShipmentDto>(dealer.Client, $"/api/v1/shipments/{back.Id}/send");
        Assert.Equal(6, await TestApi.StockOfAsync(dealer.Client, product.Id));

        var dealerReceive = await dealer.Client.PostAsJsonAsync($"/api/v1/shipments/{back.Id}/receive",
            new ReceiveShipmentRequest([new ReceiveItemRequest(back.Items[0].Id, 4, null)]), TestApi.Json);
        Assert.Equal(HttpStatusCode.Forbidden, dealerReceive.StatusCode);

        await TestApi.PostAsync<ShipmentDto>(admin, $"/api/v1/shipments/{back.Id}/receive",
            new ReceiveShipmentRequest([new ReceiveItemRequest(back.Items[0].Id, 4, null)]));
        Assert.Equal(10, await TestApi.StockOfAsync(admin, product.Id));
    }

    [Fact]
    public async Task Dealer_cannot_ship_to_another_dealer()
    {
        var dealer = await _api.CreateDealerAsync();
        var other = await _api.CreateDealerAsync();
        var (_, batch) = await _api.StockDealerAsync(dealer, 5);

        var response = await dealer.Client.PostAsJsonAsync("/api/v1/shipments", new CreateShipmentRequest(other.Dealer.Id, null, [new ShipmentItemRequest(batch.BatchId, 1)]), TestApi.Json);
        await TestApi.AssertErrorAsync(response, HttpStatusCode.BadRequest, ErrorCodes.InvalidShipmentRoute);
    }

    [Fact]
    public async Task Stock_count_makes_the_balance_match_the_shelf()
    {
        var dealer = await _api.CreateDealerAsync();
        var (product, batch) = await _api.StockDealerAsync(dealer, 20);

        var count = await TestApi.PostAsync<StockCountDto>(dealer.Client, "/api/v1/stock-counts", new CreateStockCountRequest(null));
        Assert.Contains(count.Items, i => i.BatchId == batch.BatchId && i.ExpectedQuantity == 20);

        var done = await TestApi.PostAsync<StockCountDto>(dealer.Client, $"/api/v1/stock-counts/{count.Id}/complete",
            new CompleteStockCountRequest([new CountedItemRequest(batch.BatchId, 17)]));
        Assert.Equal(StockCountStatus.Completed, done.Status);
        Assert.Equal(-3, done.Items.Single().Difference);
        Assert.Equal(17, await TestApi.StockOfAsync(dealer.Client, product.Id));

        var again = await dealer.Client.PostAsJsonAsync($"/api/v1/stock-counts/{count.Id}/complete", new CompleteStockCountRequest([new CountedItemRequest(batch.BatchId, 1)]), TestApi.Json);
        await TestApi.AssertErrorAsync(again, HttpStatusCode.Conflict, ErrorCodes.CountNotInProgress);
    }

    [Fact]
    public async Task Stock_count_must_cover_every_batch_on_the_checklist()
    {
        var dealer = await _api.CreateDealerAsync();
        await _api.StockDealerAsync(dealer, 20);
        var (_, second) = await _api.StockDealerAsync(dealer, 5);

        var count = await TestApi.PostAsync<StockCountDto>(dealer.Client, "/api/v1/stock-counts", new CreateStockCountRequest(null));
        var response = await dealer.Client.PostAsJsonAsync($"/api/v1/stock-counts/{count.Id}/complete",
            new CompleteStockCountRequest([new CountedItemRequest(second.BatchId, 5)]), TestApi.Json);

        await TestApi.AssertErrorAsync(response, HttpStatusCode.BadRequest, ErrorCodes.CountItemsMismatch);
    }

    [Fact]
    public async Task Write_off_changes_stock_only_after_admin_approval()
    {
        var admin = await _api.AdminAsync();
        var dealer = await _api.CreateDealerAsync();
        var (product, batch) = await _api.StockDealerAsync(dealer, 10);

        var request = await TestApi.PostAsync<AdjustmentDto>(dealer.Client, "/api/v1/adjustments",
            new CreateAdjustmentRequest(null, batch.BatchId, AdjustmentType.WriteOffDamaged, 2, "Singan", null, null));
        Assert.Equal(AdjustmentStatus.Pending, request.Status);
        Assert.Equal(10, await TestApi.StockOfAsync(dealer.Client, product.Id));

        var selfApprove = await dealer.Client.PostAsync($"/api/v1/adjustments/{request.Id}/approve", null);
        Assert.Equal(HttpStatusCode.Forbidden, selfApprove.StatusCode);

        await TestApi.PostAsync<AdjustmentDto>(admin, $"/api/v1/adjustments/{request.Id}/approve");
        Assert.Equal(8, await TestApi.StockOfAsync(dealer.Client, product.Id));

        var twice = await admin.PostAsync($"/api/v1/adjustments/{request.Id}/approve", null);
        await TestApi.AssertErrorAsync(twice, HttpStatusCode.Conflict, ErrorCodes.AdjustmentNotPending);
    }

    [Fact]
    public async Task Excel_import_is_all_or_nothing_and_reports_rows()
    {
        var admin = await _api.AdminAsync();
        var product = await _api.CreateProductAsync();
        var supplier = await TestApi.PostAsync<SupplierDto>(admin, "/api/v1/suppliers", new SaveSupplierRequest("Yetkazuvchi", null));
        var expiry = TestApi.Today().AddDays(200).ToString("yyyy-MM-dd");

        var good = BuildSheet([[product.Name, "X1", expiry, 15, 12.5m]]);
        var bad = BuildSheet([[product.Name, "X2", expiry, 15, 12.5m], ["Mavjud emas", "X3", "sana emas", -1, 1m]]);

        var rejected = await admin.PostAsync("/api/v1/receipts/import", ImportForm(bad, supplier.Id));
        await TestApi.AssertErrorAsync(rejected, HttpStatusCode.BadRequest, ErrorCodes.ImportFailed);
        Assert.Contains("\"row\":3", await rejected.Content.ReadAsStringAsync());
        Assert.Equal(0, await TestApi.StockOfAsync(admin, product.Id));

        var accepted = await admin.PostAsync("/api/v1/receipts/import", ImportForm(good, supplier.Id));
        var receipt = await TestApi.ReadAsync<ReceiptDto>(accepted);
        Assert.Equal(15, receipt.Items.Single().Quantity);
        Assert.Equal(15, await TestApi.StockOfAsync(admin, product.Id));
    }

    [Fact]
    public async Task Exports_produce_excel_files()
    {
        var admin = await _api.AdminAsync();
        foreach (var report in new[] { "sales", "stock", "expiring", "dealers" })
        {
            var response = await admin.GetAsync($"/api/v1/exports/{report}");
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            var bytes = await response.Content.ReadAsByteArrayAsync();
            Assert.Equal((byte)'P', bytes[0]);
            Assert.Equal((byte)'K', bytes[1]);
        }

        var unknown = await admin.GetAsync("/api/v1/exports/nope");
        await TestApi.AssertErrorAsync(unknown, HttpStatusCode.NotFound, ErrorCodes.UnknownReport);
    }

    [Fact]
    public async Task Dealer_update_replaces_service_regions()
    {
        var admin = await _api.AdminAsync();
        var provinces = await _api.ProvincesAsync();
        var dealer = await _api.CreateDealerAsync(provinces[0].Id);

        async Task Update(params Guid[] regionIds)
        {
            var response = await admin.PutAsJsonAsync($"/api/v1/dealers/{dealer.Dealer.Id}",
                new SaveDealerRequest(dealer.Dealer.Name, dealer.Dealer.Phone, provinces[0].Id, "Manzil", regionIds), TestApi.Json);
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        }

        await Update(provinces[1].Id, provinces[2].Id);
        await Update(provinces[2].Id, provinces[3].Id);

        var reloaded = await TestApi.GetAsync<DealerDto>(admin, $"/api/v1/dealers/{dealer.Dealer.Id}");
        Assert.Equal(new[] { provinces[2].Id, provinces[3].Id }.Order(), reloaded.ServiceRegionIds.Order());
    }

    [Fact]
    public async Task Journal_and_balances_always_agree()
    {
        var dealer = await _api.CreateDealerAsync();
        var pharmacy = await _api.CreatePharmacyAsync();
        var (product, _) = await _api.StockDealerAsync(dealer, 10);
        var sale = await TestApi.ReadAsync<SaleDto>(await TestApi.PostSaleAsync(dealer.Client,
            new CreateSaleRequest(pharmacy.Id, TestApi.Today(), null, [new SaleItemRequest(product.Id, 3, 150m, null)])));
        await TestApi.PostAsync<SaleDto>(dealer.Client, $"/api/v1/sales/{sale.Id}/reverse", new ReverseSaleRequest("Xato"));

        var report = await TestApi.GetAsync<ReconciliationDto>(await _api.AdminAsync(), "/api/v1/analytics/reconciliation");
        Assert.True(report.IsBalanced, string.Join(", ", report.Mismatches));
        Assert.True(report.CheckedBalances > 0);
    }

    private static byte[] BuildSheet(object[][] rows)
    {
        var sheet = new PharmaTrack.Infrastructure.Files.ClosedXmlSpreadsheet();
        return sheet.Write("Kirim", ["a", "b", "c", "d", "e"], rows.Select(r => r.Cast<object?>().ToArray()));
    }

    private static MultipartFormDataContent ImportForm(byte[] file, Guid supplierId) => new()
    {
        { new ByteArrayContent(file), "file", "kirim.xlsx" },
        { new StringContent($"DOC-{Guid.NewGuid():N}"), "documentNumber" },
        { new StringContent(supplierId.ToString()), "supplierId" },
        { new StringContent(TestApi.Today().ToString("yyyy-MM-dd")), "receivedOn" }
    };
}
