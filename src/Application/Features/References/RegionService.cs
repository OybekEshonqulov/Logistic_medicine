using FluentValidation;
using Microsoft.EntityFrameworkCore;
using PharmaTrack.Application.Common;
using PharmaTrack.Domain.Common;
using PharmaTrack.Domain.Entities;
using PharmaTrack.Domain.Enums;

namespace PharmaTrack.Application.Features.References;

public sealed record RegionDto(Guid Id, string? Code, string NameUz, string NameRu, bool IsActive);
public sealed record SaveRegionRequest(string? Code, string NameUz, string NameRu, bool IsActive = true);
public sealed record DistrictDto(Guid Id, Guid RegionId, string? Code, DistrictType Type, string NameUz, string NameRu, bool IsActive);
public sealed record SaveDistrictRequest(Guid RegionId, string? Code, DistrictType Type, string NameUz, string NameRu, bool IsActive = true);
public sealed record DistrictQuery : PageQuery
{
    public Guid? RegionId { get; init; }
    public DistrictType? Type { get; init; }
}

public sealed class SaveRegionRequestValidator : AbstractValidator<SaveRegionRequest>
{
    public SaveRegionRequestValidator()
    {
        RuleFor(x => x.Code).MaximumLength(20);
        RuleFor(x => x.NameUz).NotEmpty().MaximumLength(150);
        RuleFor(x => x.NameRu).NotEmpty().MaximumLength(150);
    }
}

public sealed class SaveDistrictRequestValidator : AbstractValidator<SaveDistrictRequest>
{
    public SaveDistrictRequestValidator()
    {
        RuleFor(x => x.RegionId).NotEmpty();
        RuleFor(x => x.Code).MaximumLength(20);
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.NameUz).NotEmpty().MaximumLength(150);
        RuleFor(x => x.NameRu).NotEmpty().MaximumLength(150);
    }
}

public sealed class RegionService(IAppDbContext db)
{
    public Task<PagedResult<RegionDto>> ListAsync(PageQuery query, CancellationToken ct)
    {
        var regions = db.Regions.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.ToLower();
            regions = regions.Where(r => r.NameUz.ToLower().Contains(search) || r.NameRu.ToLower().Contains(search));
        }

        return regions.OrderBy(r => r.NameUz)
            .Select(r => new RegionDto(r.Id, r.Code, r.NameUz, r.NameRu, r.IsActive))
            .ToPagedAsync(query, ct);
    }

    public async Task<RegionDto> GetAsync(Guid id, CancellationToken ct) =>
        ToDto(await db.Regions.AsNoTracking().Where(r => r.Id == id).FirstOrNotFoundAsync(ct));

    public async Task<RegionDto> CreateAsync(SaveRegionRequest request, CancellationToken ct)
    {
        var region = new Region { Code = request.Code, NameUz = request.NameUz, NameRu = request.NameRu, IsActive = request.IsActive };
        db.Regions.Add(region);
        await db.SaveChangesAsync(ct);
        return ToDto(region);
    }

    public async Task<RegionDto> UpdateAsync(Guid id, SaveRegionRequest request, CancellationToken ct)
    {
        var region = await db.Regions.Where(r => r.Id == id).FirstOrNotFoundAsync(ct);
        region.Code = request.Code;
        region.NameUz = request.NameUz;
        region.NameRu = request.NameRu;
        region.IsActive = request.IsActive;
        await db.SaveChangesAsync(ct);
        return ToDto(region);
    }

    public async Task DeactivateAsync(Guid id, CancellationToken ct)
    {
        var region = await db.Regions.Where(r => r.Id == id).FirstOrNotFoundAsync(ct);
        region.IsActive = false;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>The district is optional, but when given it must lie inside the region.</summary>
    public async Task EnsureValidAddressAsync(Guid regionId, Guid? districtId, CancellationToken ct)
    {
        await db.Regions.Where(r => r.Id == regionId).FirstOrNotFoundAsync(ct);
        if (districtId is { } id && !await db.Districts.AnyAsync(d => d.Id == id && d.RegionId == regionId, ct))
            throw new DomainException(ErrorCodes.DistrictNotInRegion);
    }

    private static RegionDto ToDto(Region r) => new(r.Id, r.Code, r.NameUz, r.NameRu, r.IsActive);
}

public sealed class DistrictService(IAppDbContext db)
{
    public Task<PagedResult<DistrictDto>> ListAsync(DistrictQuery query, CancellationToken ct)
    {
        var districts = db.Districts.AsNoTracking();
        if (query.RegionId is { } regionId) districts = districts.Where(d => d.RegionId == regionId);
        if (query.Type is { } type) districts = districts.Where(d => d.Type == type);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.ToLower();
            districts = districts.Where(d => d.NameUz.ToLower().Contains(search) || d.NameRu.ToLower().Contains(search));
        }

        return districts.OrderBy(d => d.NameUz)
            .Select(d => new DistrictDto(d.Id, d.RegionId, d.Code, d.Type, d.NameUz, d.NameRu, d.IsActive))
            .ToPagedAsync(query, ct);
    }

    public async Task<DistrictDto> GetAsync(Guid id, CancellationToken ct) =>
        ToDto(await db.Districts.AsNoTracking().Where(d => d.Id == id).FirstOrNotFoundAsync(ct));

    public async Task<DistrictDto> CreateAsync(SaveDistrictRequest request, CancellationToken ct)
    {
        await db.Regions.Where(r => r.Id == request.RegionId).FirstOrNotFoundAsync(ct);
        var district = new District { RegionId = request.RegionId, Code = request.Code, Type = request.Type, NameUz = request.NameUz, NameRu = request.NameRu, IsActive = request.IsActive };
        db.Districts.Add(district);
        await db.SaveChangesAsync(ct);
        return ToDto(district);
    }

    public async Task<DistrictDto> UpdateAsync(Guid id, SaveDistrictRequest request, CancellationToken ct)
    {
        var district = await db.Districts.Where(d => d.Id == id).FirstOrNotFoundAsync(ct);
        await db.Regions.Where(r => r.Id == request.RegionId).FirstOrNotFoundAsync(ct);

        district.RegionId = request.RegionId;
        district.Code = request.Code;
        district.Type = request.Type;
        district.NameUz = request.NameUz;
        district.NameRu = request.NameRu;
        district.IsActive = request.IsActive;
        await db.SaveChangesAsync(ct);
        return ToDto(district);
    }

    public async Task DeactivateAsync(Guid id, CancellationToken ct)
    {
        var district = await db.Districts.Where(d => d.Id == id).FirstOrNotFoundAsync(ct);
        district.IsActive = false;
        await db.SaveChangesAsync(ct);
    }

    private static DistrictDto ToDto(District d) => new(d.Id, d.RegionId, d.Code, d.Type, d.NameUz, d.NameRu, d.IsActive);
}
