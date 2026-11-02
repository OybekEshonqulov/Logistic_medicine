using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using PharmaTrack.Application.Common;
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
    }

    [Fact]
    public async Task Health_endpoint_reports_healthy()
    {
        var response = await _api.Anonymous().GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Provinces_are_seeded()
    {
        var page = await TestApi.GetAsync<PagedResult<PharmaTrack.Application.Features.References.RegionDto>>(await _api.AdminAsync(), "/api/v1/regions?type=Province&pageSize=50");
        Assert.Equal(14, page.TotalCount);
    }
}
