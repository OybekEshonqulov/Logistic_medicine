using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PharmaTrack.Application.Common;
using PharmaTrack.Application.Features.References;
using PharmaTrack.Infrastructure.Persistence;
using PharmaTrack.Application.Features.Auth;
using PharmaTrack.Application.Features.Users;
using PharmaTrack.Domain.Common;
using PharmaTrack.Domain.Enums;
using PharmaTrack.Tests.Support;
using Xunit;

namespace PharmaTrack.Tests;

[Collection(ApiCollection.Name)]
public sealed class AuthAndContractTests(ApiFactory factory)
{
    private readonly TestApi _api = new(factory);

    [Fact]
    public async Task Requests_without_a_token_get_a_coded_401()
    {
        var response = await _api.Anonymous().GetAsync("/api/v1/products");
        await TestApi.AssertErrorAsync(response, HttpStatusCode.Unauthorized, ErrorCodes.Unauthorized);
    }

    [Fact]
    public async Task Wrong_password_is_rejected()
    {
        var response = await _api.Anonymous().PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(ApiFactory.AdminPhone, "wrong-password"));
        await TestApi.AssertErrorAsync(response, HttpStatusCode.Unauthorized, ErrorCodes.InvalidCredentials);
    }

    [Fact]
    public async Task Me_returns_role_and_permissions()
    {
        var me = await TestApi.GetAsync<MeDto>(await _api.AdminAsync(), "/api/v1/auth/me");
        Assert.Equal(UserRole.Admin, me.Role);
        Assert.Contains("analytics.dashboard", me.Permissions);
    }

    [Fact]
    public async Task Refresh_token_rotates_and_reuse_ends_all_sessions()
    {
        var login = await TestApi.ReadAsync<AuthResponse>(await _api.Anonymous().PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(ApiFactory.AdminPhone, ApiFactory.AdminPassword)));

        var refreshed = await TestApi.ReadAsync<AuthResponse>(await _api.Anonymous().PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(login.RefreshToken)));
        Assert.NotEqual(login.RefreshToken, refreshed.RefreshToken);

        var reuse = await _api.Anonymous().PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(login.RefreshToken));
        await TestApi.AssertErrorAsync(reuse, HttpStatusCode.Unauthorized, ErrorCodes.InvalidRefreshToken);

        var afterReuse = await _api.Anonymous().PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(refreshed.RefreshToken));
        await TestApi.AssertErrorAsync(afterReuse, HttpStatusCode.Unauthorized, ErrorCodes.InvalidRefreshToken);
    }

    [Fact]
    public async Task Validation_errors_list_the_failing_fields()
    {
        var admin = await _api.AdminAsync();
        var response = await admin.PostAsJsonAsync("/api/v1/users", new CreateUserRequest("", "123", "short", UserRole.Warehouse, null), TestApi.Json);

        await TestApi.AssertErrorAsync(response, HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var errors = json.RootElement.GetProperty("errors");
        Assert.True(errors.TryGetProperty("fullName", out _));
        Assert.True(errors.TryGetProperty("phone", out _));
        Assert.True(errors.TryGetProperty("password", out _));
    }

    [Fact]
    public async Task Malformed_body_returns_the_same_error_shape()
    {
        var admin = await _api.AdminAsync();
        var response = await admin.PostAsync("/api/v1/users", new StringContent("{ not json", MediaTypeHeaderValue.Parse("application/json")));
        await TestApi.AssertErrorAsync(response, HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
    }

    [Theory]
    [InlineData("ru", "Данные не найдены")]
    [InlineData("uz", "Ma'lumot topilmadi")]
    public async Task Error_text_follows_accept_language(string language, string expected)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/products/{Guid.NewGuid()}");
        request.Headers.AcceptLanguage.ParseAdd(language);
        var response = await (await _api.AdminAsync()).SendAsync(request);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(expected, json.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Enums_are_sent_as_text()
    {
        var admin = await _api.AdminAsync();
        var json = await admin.GetStringAsync("/api/v1/auth/me");
        Assert.Contains("\"role\":\"Admin\"", json);
    }

    [Fact]
    public async Task Duplicate_phone_is_a_conflict()
    {
        var admin = await _api.AdminAsync();
        var response = await admin.PostAsJsonAsync("/api/v1/users", new CreateUserRequest("Ikkinchi", ApiFactory.AdminPhone, "Password-123", UserRole.Warehouse, null), TestApi.Json);
        await TestApi.AssertErrorAsync(response, HttpStatusCode.Conflict, ErrorCodes.PhoneTaken);
    }

    [Fact]
    public async Task Deactivated_user_cannot_log_in()
    {
        var admin = await _api.AdminAsync();
        var phone = TestApi.NextPhone();
        var user = await TestApi.PostAsync<UserDto>(admin, "/api/v1/users", new CreateUserRequest("Vaqtinchalik", phone, "Password-123", UserRole.Warehouse, null));
        var update = await admin.PutAsJsonAsync($"/api/v1/users/{user.Id}", new UpdateUserRequest("Vaqtinchalik", phone, UserRole.Warehouse, null, false), TestApi.Json);
        update.EnsureSuccessStatusCode();

        var response = await _api.Anonymous().PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(phone, "Password-123"));
        await TestApi.AssertErrorAsync(response, HttpStatusCode.Forbidden, ErrorCodes.UserInactive);
    }

    [Fact]
    public async Task Swagger_document_is_generated_for_clients()
    {
        var response = await _api.Anonymous().GetAsync("/swagger/v1/swagger.json");
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, text);
        Assert.Contains("/api/v1/analytics/dashboard", text);
        Assert.Contains("/api/v1/sales", text);

        using var json = JsonDocument.Parse(text);
        Assert.Equal("/", json.RootElement.GetProperty("servers")[0].GetProperty("url").GetString());
    }

    [Fact]
    public async Task Health_endpoint_reports_healthy()
    {
        var response = await _api.Anonymous().GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Regions_and_districts_are_seeded_in_their_own_tables()
    {
        var admin = await _api.AdminAsync();
        var regions = await TestApi.GetAsync<PagedResult<RegionDto>>(admin, "/api/v1/regions?pageSize=50");
        Assert.Equal(14, regions.TotalCount);

        var districts = await TestApi.GetAsync<PagedResult<DistrictDto>>(admin, "/api/v1/districts?pageSize=200");
        Assert.Equal(200, districts.Items.Count);
        Assert.True(districts.TotalCount >= 209);

        var samarkand = regions.Items.Single(r => r.NameUz.StartsWith("Samarqand"));
        var samarkandDistricts = await TestApi.GetAsync<PagedResult<DistrictDto>>(admin, $"/api/v1/districts?regionId={samarkand.Id}&pageSize=50");
        Assert.Contains(samarkandDistricts.Items, d => d.NameUz == "Samarqand shahri" && d.Type == DistrictType.City);
        Assert.Contains(samarkandDistricts.Items, d => d.NameUz == "Urgut tumani" && d.Type == DistrictType.District);
    }

    [Fact]
    public async Task Seeding_twice_adds_nothing_and_keeps_edited_names()
    {
        var admin = await _api.AdminAsync();
        var regions = await TestApi.GetAsync<PagedResult<RegionDto>>(admin, "/api/v1/regions?pageSize=50");
        var region = regions.Items.Single(r => r.NameUz.StartsWith("Sirdaryo"));
        var page = await TestApi.GetAsync<PagedResult<DistrictDto>>(admin, $"/api/v1/districts?regionId={region.Id}&pageSize=50");
        var district = page.Items.Single(d => d.NameUz == "Sardoba tumani");
        var update = await admin.PutAsJsonAsync($"/api/v1/districts/{district.Id}",
            new SaveDistrictRequest(region.Id, district.Code, district.Type, district.NameUz, "Изменённое имя"), TestApi.Json);
        update.EnsureSuccessStatusCode();
        var before = await TestApi.GetAsync<PagedResult<DistrictDto>>(admin, "/api/v1/districts?pageSize=1");

        await using (var scope = factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<DbSeeder>().SeedAsync();

        var after = await TestApi.GetAsync<PagedResult<DistrictDto>>(admin, "/api/v1/districts?pageSize=1");
        Assert.Equal(before.TotalCount, after.TotalCount);
        var reloaded = await TestApi.GetAsync<DistrictDto>(admin, $"/api/v1/districts/{district.Id}");
        Assert.Equal("Изменённое имя", reloaded.NameRu);
    }

    [Fact]
    public async Task District_must_belong_to_the_chosen_region()
    {
        var admin = await _api.AdminAsync();
        var regions = await _api.ProvincesAsync();
        var other = await TestApi.GetAsync<PagedResult<DistrictDto>>(admin, $"/api/v1/districts?regionId={regions[1].Id}&pageSize=1");

        var response = await admin.PostAsJsonAsync("/api/v1/pharmacies",
            new SavePharmacyRequest("Dorixona", regions[0].Id, other.Items[0].Id, "Manzil"), TestApi.Json);

        await TestApi.AssertErrorAsync(response, HttpStatusCode.BadRequest, ErrorCodes.DistrictNotInRegion);
    }
}
