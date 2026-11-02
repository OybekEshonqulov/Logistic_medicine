using System.Net;
using System.Net.Http.Json;
using PharmaTrack.Application.Common;
using PharmaTrack.Application.Features.References;
using PharmaTrack.Application.Features.Sales;
using PharmaTrack.Application.Features.Stock;
using PharmaTrack.Domain.Common;
using PharmaTrack.Domain.Enums;
using PharmaTrack.Tests.Support;
using Xunit;

namespace PharmaTrack.Tests;

[Collection(ApiCollection.Name)]
public sealed class SalesTests(ApiFactory factory)
{
    private readonly TestApi _api = new(factory);

    private static CreateSaleRequest Sale(Guid pharmacyId, Guid productId, int quantity, Guid? batchId = null, DateOnly? date = null) =>
        new(pharmacyId, date ?? TestApi.Today(), null, [new SaleItemRequest(productId, quantity, 150m, batchId)]);

    [Fact]
    public async Task Sale_takes_the_earliest_expiring_batch_first()
    {
        var dealer = await _api.CreateDealerAsync();
        var pharmacy = await _api.CreatePharmacyAsync();
        var product = await _api.CreateProductAsync();
        var later = await _api.ReceiveAsync(product.Id, "LATER", TestApi.Today().AddDays(300), 10, 10m);
        var sooner = await _api.ReceiveAsync(product.Id, "SOONER", TestApi.Today().AddDays(30), 10, 10m);
        await _api.ShipAndReceiveAsync(dealer, later.BatchId, 10);
        await _api.ShipAndReceiveAsync(dealer, sooner.BatchId, 10);

        var sale = await TestApi.ReadAsync<SaleDto>(await TestApi.PostSaleAsync(dealer.Client, Sale(pharmacy.Id, product.Id, 15)));

        var batches = sale.Items.Single().Batches.ToDictionary(b => b.BatchNumber, b => b.Quantity);
        Assert.Equal(10, batches["SOONER"]);
        Assert.Equal(5, batches["LATER"]);
        Assert.Equal(5, await TestApi.StockOfAsync(dealer.Client, product.Id));
        Assert.Equal(15 * 150m, sale.TotalAmount);
    }

    [Fact]
    public async Task Dealer_may_choose_the_batch()
    {
        var dealer = await _api.CreateDealerAsync();
        var pharmacy = await _api.CreatePharmacyAsync();
        var product = await _api.CreateProductAsync();
        var later = await _api.ReceiveAsync(product.Id, "LATER", TestApi.Today().AddDays(300), 10, 10m);
        var sooner = await _api.ReceiveAsync(product.Id, "SOONER", TestApi.Today().AddDays(30), 10, 10m);
        await _api.ShipAndReceiveAsync(dealer, later.BatchId, 10);
        await _api.ShipAndReceiveAsync(dealer, sooner.BatchId, 10);

        var sale = await TestApi.ReadAsync<SaleDto>(await TestApi.PostSaleAsync(dealer.Client, Sale(pharmacy.Id, product.Id, 4, later.BatchId)));
        Assert.Equal("LATER", sale.Items.Single().Batches.Single().BatchNumber);
    }

    [Fact]
    public async Task Cannot_sell_more_than_is_on_hand()
    {
        var dealer = await _api.CreateDealerAsync();
        var pharmacy = await _api.CreatePharmacyAsync();
        var (product, _) = await _api.StockDealerAsync(dealer, 5);

        var response = await TestApi.PostSaleAsync(dealer.Client, Sale(pharmacy.Id, product.Id, 6));

        await TestApi.AssertErrorAsync(response, HttpStatusCode.Conflict, ErrorCodes.InsufficientStock);
        Assert.Equal(5, await TestApi.StockOfAsync(dealer.Client, product.Id));
    }

    [Fact]
    public async Task Failed_line_rolls_back_the_whole_sale()
    {
        var dealer = await _api.CreateDealerAsync();
        var pharmacy = await _api.CreatePharmacyAsync();
        var (first, _) = await _api.StockDealerAsync(dealer, 5);
        var (second, _) = await _api.StockDealerAsync(dealer, 1);

        var response = await TestApi.PostSaleAsync(dealer.Client, new CreateSaleRequest(pharmacy.Id, TestApi.Today(), null,
            [new SaleItemRequest(first.Id, 2, 100m, null), new SaleItemRequest(second.Id, 2, 100m, null)]));

        await TestApi.AssertErrorAsync(response, HttpStatusCode.Conflict, ErrorCodes.InsufficientStock);
        Assert.Equal(5, await TestApi.StockOfAsync(dealer.Client, first.Id));
    }

    [Fact]
    public async Task Parallel_sales_never_oversell()
    {
        var dealer = await _api.CreateDealerAsync();
        var pharmacy = await _api.CreatePharmacyAsync();
        var (product, _) = await _api.StockDealerAsync(dealer, 10);

        var attempts = Enumerable.Range(0, 20).Select(_ => TestApi.PostSaleAsync(dealer.Client, Sale(pharmacy.Id, product.Id, 1))).ToList();
        var responses = await Task.WhenAll(attempts);

        Assert.Equal(10, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(10, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(0, await TestApi.StockOfAsync(dealer.Client, product.Id));
    }

    [Fact]
    public async Task Retry_with_the_same_key_does_not_duplicate_the_sale()
    {
        var dealer = await _api.CreateDealerAsync();
        var pharmacy = await _api.CreatePharmacyAsync();
        var (product, _) = await _api.StockDealerAsync(dealer, 10);
        var key = Guid.NewGuid().ToString();

        var first = await TestApi.PostSaleAsync(dealer.Client, Sale(pharmacy.Id, product.Id, 3), key);
        var retry = await TestApi.PostSaleAsync(dealer.Client, Sale(pharmacy.Id, product.Id, 3), key);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal((await TestApi.ReadAsync<SaleDto>(first)).Id, (await TestApi.ReadAsync<SaleDto>(retry)).Id);
        Assert.Equal(7, await TestApi.StockOfAsync(dealer.Client, product.Id));
    }

    [Fact]
    public async Task Simultaneous_retries_create_one_sale()
    {
        var dealer = await _api.CreateDealerAsync();
        var pharmacy = await _api.CreatePharmacyAsync();
        var (product, _) = await _api.StockDealerAsync(dealer, 10);
        var key = Guid.NewGuid().ToString();

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => TestApi.PostSaleAsync(dealer.Client, Sale(pharmacy.Id, product.Id, 2), key)));

        Assert.All(responses, r => Assert.True(r.IsSuccessStatusCode, r.StatusCode.ToString()));
        Assert.Equal(8, await TestApi.StockOfAsync(dealer.Client, product.Id));
    }

    [Fact]
    public async Task Sale_requires_an_idempotency_key()
    {
        var dealer = await _api.CreateDealerAsync();
        var pharmacy = await _api.CreatePharmacyAsync();
        var product = await _api.CreateProductAsync();

        var response = await dealer.Client.PostAsJsonAsync("/api/v1/sales", Sale(pharmacy.Id, product.Id, 1), TestApi.Json);
        await TestApi.AssertErrorAsync(response, HttpStatusCode.BadRequest, ErrorCodes.IdempotencyKeyRequired);
    }

    [Fact]
    public async Task Reversal_restores_stock_once()
    {
        var dealer = await _api.CreateDealerAsync();
        var pharmacy = await _api.CreatePharmacyAsync();
        var (product, _) = await _api.StockDealerAsync(dealer, 10);
        var sale = await TestApi.ReadAsync<SaleDto>(await TestApi.PostSaleAsync(dealer.Client, Sale(pharmacy.Id, product.Id, 4)));

        var reversed = await TestApi.PostAsync<SaleDto>(dealer.Client, $"/api/v1/sales/{sale.Id}/reverse", new ReverseSaleRequest("Noto'g'ri kiritilgan"));
        Assert.Equal(SaleStatus.Reversed, reversed.Status);
        Assert.Equal(10, await TestApi.StockOfAsync(dealer.Client, product.Id));

        var twice = await dealer.Client.PostAsJsonAsync($"/api/v1/sales/{sale.Id}/reverse", new ReverseSaleRequest("Yana"), TestApi.Json);
        await TestApi.AssertErrorAsync(twice, HttpStatusCode.Conflict, ErrorCodes.SaleAlreadyReversed);
        Assert.Equal(10, await TestApi.StockOfAsync(dealer.Client, product.Id));
    }

    [Fact]
    public async Task Future_sale_date_is_rejected()
    {
        var dealer = await _api.CreateDealerAsync();
        var pharmacy = await _api.CreatePharmacyAsync();
        var (product, _) = await _api.StockDealerAsync(dealer, 5);

        var response = await TestApi.PostSaleAsync(dealer.Client, Sale(pharmacy.Id, product.Id, 1, date: TestApi.Today().AddDays(2)));
        await TestApi.AssertErrorAsync(response, HttpStatusCode.BadRequest, ErrorCodes.SaleDateInFuture);
    }

    [Fact]
    public async Task Offline_sale_with_a_past_date_is_accepted()
    {
        var dealer = await _api.CreateDealerAsync();
        var pharmacy = await _api.CreatePharmacyAsync();
        var (product, _) = await _api.StockDealerAsync(dealer, 5);

        var sale = await TestApi.ReadAsync<SaleDto>(await TestApi.PostSaleAsync(dealer.Client, Sale(pharmacy.Id, product.Id, 1, date: TestApi.Today().AddDays(-3))));
        Assert.Equal(TestApi.Today().AddDays(-3), sale.SaleDate);
    }

    [Fact]
    public async Task Dealer_can_sell_to_their_own_pending_pharmacy_but_not_to_someone_elses()
    {
        var dealer = await _api.CreateDealerAsync();
        var other = await _api.CreateDealerAsync();
        var regionId = (await _api.ProvincesAsync())[0].Id;
        var pending = await TestApi.PostAsync<PharmacyDto>(dealer.Client, "/api/v1/pharmacies", new SavePharmacyRequest("Yangi dorixona", regionId, "Manzil", null));
        Assert.Equal(PharmacyStatus.Pending, pending.Status);

        var (product, _) = await _api.StockDealerAsync(dealer, 5);
        var (otherProduct, _) = await _api.StockDealerAsync(other, 5);

        var own = await TestApi.PostSaleAsync(dealer.Client, Sale(pending.Id, product.Id, 1));
        Assert.Equal(HttpStatusCode.Created, own.StatusCode);

        var foreign = await TestApi.PostSaleAsync(other.Client, Sale(pending.Id, otherProduct.Id, 1));
        await TestApi.AssertErrorAsync(foreign, HttpStatusCode.BadRequest, ErrorCodes.PharmacyNotAvailable);
    }

    [Fact]
    public async Task Dealers_never_see_each_others_data()
    {
        var dealer = await _api.CreateDealerAsync();
        var other = await _api.CreateDealerAsync();
        var pharmacy = await _api.CreatePharmacyAsync();
        var (product, batch) = await _api.StockDealerAsync(dealer, 5);
        var sale = await TestApi.ReadAsync<SaleDto>(await TestApi.PostSaleAsync(dealer.Client, Sale(pharmacy.Id, product.Id, 1)));

        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.GetAsync($"/api/v1/sales/{sale.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.GetAsync($"/api/v1/dealers/{dealer.Dealer.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.PostAsJsonAsync($"/api/v1/sales/{sale.Id}/reverse", new ReverseSaleRequest("x"), TestApi.Json)).StatusCode);

        var sales = await TestApi.GetAsync<PagedResult<SaleDto>>(other.Client, $"/api/v1/sales?dealerId={dealer.Dealer.Id}");
        Assert.Empty(sales.Items);
        Assert.Equal(0, await TestApi.StockOfAsync(other.Client, product.Id, dealer.Dealer.Id));

        var shipments = await TestApi.GetAsync<PagedResult<ShipmentDto>>(other.Client, "/api/v1/shipments");
        Assert.Empty(shipments.Items);
    }

    [Fact]
    public async Task Dealer_does_not_receive_purchase_prices()
    {
        var dealer = await _api.CreateDealerAsync();
        var (_, batch) = await _api.StockDealerAsync(dealer, 5, price: 77m);

        var seenByDealer = await TestApi.GetAsync<BatchDto>(dealer.Client, $"/api/v1/batches/{batch.BatchId}");
        Assert.Null(seenByDealer.PurchasePrice);

        var seenByAdmin = await TestApi.GetAsync<BatchDto>(await _api.AdminAsync(), $"/api/v1/batches/{batch.BatchId}");
        Assert.Equal(77m, seenByAdmin.PurchasePrice);
    }

    [Fact]
    public async Task Sync_gives_the_mobile_app_references_and_own_stock()
    {
        var dealer = await _api.CreateDealerAsync();
        var (product, _) = await _api.StockDealerAsync(dealer, 5);

        var sync = await TestApi.GetAsync<PharmaTrack.Application.Features.Sync.SyncResponse>(dealer.Client, "/api/v1/sync/references");
        Assert.Contains(sync.Products, p => p.Id == product.Id);
        Assert.Equal(5, sync.MyStock.Single(s => s.ProductId == product.Id).Quantity);

        var since = Uri.EscapeDataString(sync.ServerTime.AddMinutes(1).ToString("O"));
        var nothingNew = await TestApi.GetAsync<PharmaTrack.Application.Features.Sync.SyncResponse>(dealer.Client, $"/api/v1/sync/references?updatedSince={since}");
        Assert.Empty(nothingNew.Products);
    }
}
