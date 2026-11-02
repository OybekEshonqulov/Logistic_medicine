using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using PharmaTrack.Application.Common;
using PharmaTrack.Domain.Entities;
using PharmaTrack.Domain.Enums;

namespace PharmaTrack.Infrastructure.Persistence;

public sealed class DbSeeder(AppDbContext db, IPasswordService passwords, IConfiguration configuration)
{
    private static readonly (string Uz, string Ru)[] Provinces =
    [
        ("Qoraqalpog'iston Respublikasi", "Республика Каракалпакстан"),
        ("Andijon viloyati", "Андижанская область"),
        ("Buxoro viloyati", "Бухарская область"),
        ("Farg'ona viloyati", "Ферганская область"),
        ("Jizzax viloyati", "Джизакская область"),
        ("Xorazm viloyati", "Хорезмская область"),
        ("Namangan viloyati", "Наманганская область"),
        ("Navoiy viloyati", "Навоийская область"),
        ("Qashqadaryo viloyati", "Кашкадарьинская область"),
        ("Samarqand viloyati", "Самаркандская область"),
        ("Sirdaryo viloyati", "Сырдарьинская область"),
        ("Surxondaryo viloyati", "Сурхандарьинская область"),
        ("Toshkent viloyati", "Ташкентская область"),
        ("Toshkent shahri", "город Ташкент")
    ];

    public async Task SeedAsync(CancellationToken ct = default)
    {
        await db.Database.MigrateAsync(ct);

        if (!await db.StockLocations.AnyAsync(l => l.Id == StockLocation.CentralId, ct))
            db.StockLocations.Add(new StockLocation(StockLocation.CentralId) { Type = LocationType.Central, Name = "Markaziy ombor" });

        if (!await db.Regions.AnyAsync(ct))
        {
            db.Regions.AddRange(Provinces.Select(p => new Region { Type = RegionType.Province, NameUz = p.Uz, NameRu = p.Ru }));
        }

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
}
