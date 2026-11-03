using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmaTrack.Application.Common;
using PharmaTrack.Application.Features.References;
using PharmaTrack.Application.Features.Users;
using PharmaTrack.Domain.Entities;
using PharmaTrack.Domain.Enums;
using PharmaTrack.Infrastructure.Persistence;
using PharmaTrack.Tests.Support;
using Xunit;

namespace PharmaTrack.Tests;

[Collection(ApiCollection.Name)]
public sealed partial class LanguageTests(ApiFactory factory)
{
    private readonly TestApi _api = new(factory);

    [GeneratedRegex(@"\p{IsCyrillic}")]
    private static partial Regex Cyrillic();

    [Theory]
    [InlineData("uz", false)]
    [InlineData("uz-Cyrl", true)]
    [InlineData("uz-Cyrl-UZ", true)]
    [InlineData("ru", true)]
    public async Task Validation_messages_follow_the_language(string language, bool cyrillic)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/users")
        {
            Content = JsonContent.Create(new CreateUserRequest("", "123", "short", UserRole.Warehouse, null), options: TestApi.Json)
        };
        request.Headers.AcceptLanguage.ParseAdd(language);
        var response = await (await _api.AdminAsync()).SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var message = json.RootElement.GetProperty("errors").GetProperty("fullName")[0].GetString()!;
        Assert.Equal(cyrillic, Cyrillic().IsMatch(message));
        Assert.DoesNotContain("must not be empty", message);
    }

    [Fact]
    public async Task Seeded_regions_and_districts_have_cyrillic_names()
    {
        var admin = await _api.AdminAsync();
        var regions = await TestApi.GetAsync<PagedResult<RegionDto>>(admin, "/api/v1/regions?pageSize=50");
        Assert.All(regions.Items, r => Assert.Matches(Cyrillic(), r.NameCyrl));

        var samarkand = regions.Items.Single(r => r.NameUz.StartsWith("Samarqand"));
        Assert.Equal("Самарқанд вилояти", samarkand.NameCyrl);
        var districts = await TestApi.GetAsync<PagedResult<DistrictDto>>(admin, $"/api/v1/districts?regionId={samarkand.Id}&pageSize=50");
        Assert.Contains(districts.Items, d => d.NameCyrl == "Самарқанд шаҳри");

        var found = await TestApi.GetAsync<PagedResult<RegionDto>>(admin, "/api/v1/regions?search=Самарқанд");
        Assert.Equal(samarkand.Id, Assert.Single(found.Items).Id);
    }

    [Fact]
    public async Task Seeding_fills_only_empty_cyrillic_names()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Regions.Where(r => r.NameUz == "Xorazm viloyati").ExecuteUpdateAsync(s => s.SetProperty(r => r.NameCyrl, ""));
        await db.Districts.Where(d => d.NameUz == "Xiva tumani").ExecuteUpdateAsync(s => s.SetProperty(d => d.NameCyrl, ""));
        await db.Districts.Where(d => d.NameUz == "Urganch tumani").ExecuteUpdateAsync(s => s.SetProperty(d => d.NameCyrl, "Таҳрирланган"));

        await scope.ServiceProvider.GetRequiredService<DbSeeder>().SeedAsync();

        db.ChangeTracker.Clear();
        Assert.Equal("Хоразм вилояти", (await db.Regions.SingleAsync(r => r.NameUz == "Xorazm viloyati")).NameCyrl);
        Assert.Equal("Хива тумани", (await db.Districts.SingleAsync(d => d.NameUz == "Xiva tumani")).NameCyrl);
        Assert.Equal("Таҳрирланган", (await db.Districts.SingleAsync(d => d.NameUz == "Urganch tumani")).NameCyrl);
    }

    [Theory]
    [InlineData("uz", "Navoiy viloyati")]
    [InlineData("uz-Cyrl", "Навоий вилояти")]
    [InlineData("ru", "Навоийская область")]
    public async Task Region_name_on_a_record_follows_the_language(string language, string expected)
    {
        var admin = await _api.AdminAsync();
        var region = (await _api.ProvincesAsync()).Single(r => r.NameUz == "Navoiy viloyati");
        var pharmacy = await TestApi.PostAsync<PharmacyDto>(admin, "/api/v1/pharmacies",
            new SavePharmacyRequest("Til dorixonasi " + Guid.NewGuid(), region.Id, null, "Manzil"));

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/pharmacies/{pharmacy.Id}");
        request.Headers.AcceptLanguage.ParseAdd(language);
        var response = await admin.SendAsync(request);

        var reloaded = await TestApi.ReadAsync<PharmacyDto>(response);
        Assert.Equal(expected, reloaded.RegionName);
    }

    [Theory]
    [InlineData("uz")]
    [InlineData("uz-Cyrl")]
    [InlineData("ru")]
    public async Task Excel_files_are_generated_in_every_language(string language)
    {
        var admin = await _api.AdminAsync();
        foreach (var path in new[] { "exports/sales", "exports/stock", "exports/expiring", "exports/dealers", "receipts/import/template" })
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/{path}");
            request.Headers.AcceptLanguage.ParseAdd(language);
            var response = await admin.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Theory]
    [InlineData("uz", "Markaziy ombor")]
    [InlineData("uz-Cyrl", "Марказий омбор")]
    [InlineData("ru", "Центральный склад")]
    public void Central_store_name_is_translated_but_a_dealer_name_is_not(string language, string expected)
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = new CultureInfo(language);
        try
        {
            Assert.Equal(expected, Texts.LocationName(StockLocation.CentralId, "Markaziy ombor"));
            Assert.Equal("Diler MChJ", Texts.LocationName(Guid.NewGuid(), "Diler MChJ"));
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }
}
