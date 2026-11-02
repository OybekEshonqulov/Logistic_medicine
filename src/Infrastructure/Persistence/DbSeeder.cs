using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using PharmaTrack.Application.Common;
using PharmaTrack.Domain.Entities;
using PharmaTrack.Domain.Enums;

namespace PharmaTrack.Infrastructure.Persistence;

public sealed class DbSeeder(AppDbContext db, IPasswordService passwords, IConfiguration configuration)
{
    private const string RegionsResource = "PharmaTrack.Infrastructure.Persistence.Seed.regions.json";

    private sealed record SeedDistrict(string? Code, DistrictType Type, string NameUz, string NameRu);
    private sealed record SeedRegion(string Code, string NameUz, string NameRu, List<SeedDistrict> Districts);

    public async Task SeedAsync(CancellationToken ct = default)
    {
        await db.Database.MigrateAsync(ct);

        if (!await db.StockLocations.AnyAsync(l => l.Id == StockLocation.CentralId, ct))
            db.StockLocations.Add(new StockLocation(StockLocation.CentralId) { Type = LocationType.Central, Name = "Markaziy ombor" });

        await SeedRegionsAsync(ct);

        // The first administrator comes only from configuration, so no password ever lives in the repository.
        var phone = configuration["Seed:AdminPhone"];
        var password = configuration["Seed:AdminPassword"];
        if (!string.IsNullOrWhiteSpace(phone) && !string.IsNullOrWhiteSpace(password)
            && !await db.Users.AnyAsync(u => u.Role == UserRole.Admin, ct))
        {
            db.Users.Add(new User { FullName = "Administrator", Phone = phone, PasswordHash = passwords.Hash(password), Role = UserRole.Admin });
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>Adds only what is missing, so names edited by an administrator are never overwritten.</summary>
    private async Task SeedRegionsAsync(CancellationToken ct)
    {
        var existingRegions = await db.Regions.ToDictionaryAsync(r => r.NameUz, ct);
        var existingDistricts = await db.Districts.Select(d => new { d.RegionId, d.NameUz }).ToListAsync(ct);
        var known = existingDistricts.Select(d => (d.RegionId, d.NameUz)).ToHashSet();

        foreach (var seed in await ReadSeedAsync(ct))
        {
            if (!existingRegions.TryGetValue(seed.NameUz, out var region))
            {
                region = new Region { Code = seed.Code, NameUz = seed.NameUz, NameRu = seed.NameRu };
                db.Regions.Add(region);
            }

            db.Districts.AddRange(seed.Districts
                .Where(d => !known.Contains((region.Id, d.NameUz)))
                .Select(d => new District { RegionId = region.Id, Code = d.Code, Type = d.Type, NameUz = d.NameUz, NameRu = d.NameRu }));
        }
    }

    private static async Task<List<SeedRegion>> ReadSeedAsync(CancellationToken ct)
    {
        await using var stream = typeof(DbSeeder).Assembly.GetManifestResourceStream(RegionsResource)
            ?? throw new InvalidOperationException($"Embedded resource {RegionsResource} not found.");

        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
        return await JsonSerializer.DeserializeAsync<List<SeedRegion>>(stream, options, ct) ?? [];
    }
}
