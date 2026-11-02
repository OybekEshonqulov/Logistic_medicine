using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using PharmaTrack.Application.Common;
using PharmaTrack.Application.Features.Auth;
using PharmaTrack.Application.Features.Users;
using PharmaTrack.Application.Features.References;
using PharmaTrack.Application.Features.Sales;
using PharmaTrack.Application.Features.Stock;
using PharmaTrack.Domain.Enums;
using Xunit;

namespace PharmaTrack.Tests.Support;

public sealed record CreatedDealer(DealerDto Dealer, HttpClient Client);

/// <summary>Builds test data through the public API, exactly as a real client would.</summary>
public sealed class TestApi(ApiFactory factory)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static int _phoneCounter;
    private HttpClient? _admin;

    public HttpClient Anonymous() => factory.CreateClient();

    public async Task<HttpClient> AdminAsync() =>
        _admin ??= await LoginAsync(ApiFactory.AdminPhone, ApiFactory.AdminPassword);

    public async Task<HttpClient> LoginAsync(string phone, string password)
    {
        var response = await Anonymous().PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(phone, password));
        var auth = await ReadAsync<AuthResponse>(response);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return client;
    }

    public static string NextPhone() => $"+99891{Interlocked.Increment(ref _phoneCounter) + Random.Shared.Next(1000) * 10000:D7}";

    public async Task<HttpClient> CreateUserAsync(UserRole role, Guid? dealerId = null)
    {
        var admin = await AdminAsync();
        var phone = NextPhone();
        const string password = "User-Test-123";
        await PostAsync<UserDto>(admin, "/api/v1/users", new CreateUserRequest("Test User", phone, password, role, dealerId));
        return await LoginAsync(phone, password);
    }

    public async Task<IReadOnlyList<RegionDto>> ProvincesAsync()
    {
        var admin = await AdminAsync();
        var page = await GetAsync<PagedResult<RegionDto>>(admin, "/api/v1/regions?pageSize=50");
        return page.Items;
    }

    public async Task<CreatedDealer> CreateDealerAsync(Guid? regionId = null, Guid? districtId = null)
    {
        var admin = await AdminAsync();
        regionId ??= (await ProvincesAsync())[0].Id;
        var dealer = await PostAsync<DealerDto>(admin, "/api/v1/dealers",
            new SaveDealerRequest($"Diler {Guid.NewGuid():N}", NextPhone(), regionId.Value, districtId, "Manzil"));
        return new CreatedDealer(dealer, await CreateUserAsync(UserRole.Dealer, dealer.Id));
    }

    public async Task<PharmacyDto> CreatePharmacyAsync(Guid? regionId = null, Guid? districtId = null)
    {
        var admin = await AdminAsync();
        regionId ??= (await ProvincesAsync())[0].Id;
        return await PostAsync<PharmacyDto>(admin, "/api/v1/pharmacies",
            new SavePharmacyRequest($"Dorixona {Guid.NewGuid():N}", regionId.Value, districtId, "Manzil"));
    }

    public async Task<ProductDto> CreateProductAsync()
    {
        var admin = await AdminAsync();
        var manufacturer = await PostAsync<ManufacturerDto>(admin, "/api/v1/manufacturers", new SaveManufacturerRequest($"Zavod {Guid.NewGuid():N}", "UZ"));
        return await PostAsync<ProductDto>(admin, "/api/v1/products", new SaveProductRequest($"Dori {Guid.NewGuid():N}", "Tabletka", "500 mg", manufacturer.Id, null));
    }

    public async Task<ReceiptItemDto> ReceiveAsync(Guid productId, string batchNumber, DateOnly expiry, int quantity, decimal price)
    {
        var admin = await AdminAsync();
        var supplier = await PostAsync<SupplierDto>(admin, "/api/v1/suppliers", new SaveSupplierRequest($"Yetkazuvchi {Guid.NewGuid():N}", null));
        var receipt = await PostAsync<ReceiptDto>(admin, "/api/v1/receipts", new CreateReceiptRequest(
            $"DOC-{Guid.NewGuid():N}", supplier.Id, DateOnly.FromDateTime(DateTime.UtcNow), null,
            [new ReceiptItemRequest(productId, batchNumber, expiry, quantity, price)]));
        return receipt.Items[0];
    }

    public async Task<ShipmentDto> ShipAsync(Guid dealerId, Guid batchId, int quantity)
    {
        var admin = await AdminAsync();
        var shipment = await PostAsync<ShipmentDto>(admin, "/api/v1/shipments", new CreateShipmentRequest(dealerId, null, [new ShipmentItemRequest(batchId, quantity)]));
        await PostAsync<ShipmentDto>(admin, $"/api/v1/shipments/{shipment.Id}/approve");
        return await PostAsync<ShipmentDto>(admin, $"/api/v1/shipments/{shipment.Id}/send");
    }

    public async Task<ShipmentDto> ShipAndReceiveAsync(CreatedDealer dealer, Guid batchId, int quantity)
    {
        var shipment = await ShipAsync(dealer.Dealer.Id, batchId, quantity);
        return await PostAsync<ShipmentDto>(dealer.Client, $"/api/v1/shipments/{shipment.Id}/receive",
            new ReceiveShipmentRequest([new ReceiveItemRequest(shipment.Items[0].Id, quantity, null)]));
    }

    /// <summary>Stocks a dealer with one product and returns the batch.</summary>
    public async Task<(ProductDto Product, ReceiptItemDto Batch)> StockDealerAsync(CreatedDealer dealer, int quantity, decimal price = 100m, int shelfLifeDays = 365)
    {
        var product = await CreateProductAsync();
        var batch = await ReceiveAsync(product.Id, "B-1", Today().AddDays(shelfLifeDays), quantity, price);
        await ShipAndReceiveAsync(dealer, batch.BatchId, quantity);
        return (product, batch);
    }

    public static Task<HttpResponseMessage> PostSaleAsync(HttpClient client, CreateSaleRequest request, string? key = null)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, "/api/v1/sales") { Content = JsonContent.Create(request, options: Json) };
        message.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString());
        return client.SendAsync(message);
    }

    public static async Task<int> StockOfAsync(HttpClient client, Guid productId, Guid? dealerId = null)
    {
        var url = $"/api/v1/stock/balances?productId={productId}&pageSize=200" + (dealerId is null ? string.Empty : $"&dealerId={dealerId}");
        var page = await GetAsync<PagedResult<BalanceDto>>(client, url);
        return page.Items.Sum(b => b.Quantity);
    }

    public static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(5));

    public static async Task<T> GetAsync<T>(HttpClient client, string url) => await ReadAsync<T>(await client.GetAsync(url));

    public static async Task<T> PostAsync<T>(HttpClient client, string url, object? body = null) =>
        await ReadAsync<T>(await client.PostAsJsonAsync(url, body ?? new { }, Json));

    public static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode} {response.RequestMessage?.RequestUri}: {text}");
        return JsonSerializer.Deserialize<T>(text, Json)!;
    }

    public static async Task AssertErrorAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == status, $"Expected {status}, got {response.StatusCode}: {text}");
        using var json = JsonDocument.Parse(text);
        Assert.Equal(code, json.RootElement.GetProperty("code").GetString());
    }
}
