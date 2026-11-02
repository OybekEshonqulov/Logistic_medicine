using FluentValidation;
using Microsoft.EntityFrameworkCore;
using PharmaTrack.Application.Common;
using PharmaTrack.Domain.Common;
using PharmaTrack.Domain.Entities;
using PharmaTrack.Domain.Enums;

namespace PharmaTrack.Application.Features.References;

public sealed record RegionDto(Guid Id, Guid? ParentId, RegionType Type, string NameUz, string NameRu, bool IsActive);
public sealed record SaveRegionRequest(Guid? ParentId, RegionType Type, string NameUz, string NameRu, bool IsActive = true);
public sealed record RegionQuery : PageQuery
{
    public Guid? ParentId { get; init; }
    public RegionType? Type { get; init; }
}

public sealed class SaveRegionRequestValidator : AbstractValidator<SaveRegionRequest>
{
    public SaveRegionRequestValidator()
    {
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.NameUz).NotEmpty().MaximumLength(150);
        RuleFor(x => x.NameRu).NotEmpty().MaximumLength(150);
    }
}

public sealed class RegionService(IAppDbContext db)
{
    public Task<PagedResult<RegionDto>> ListAsync(RegionQuery query, CancellationToken ct)
    {
        var regions = db.Regions.AsNoTracking();
        if (query.ParentId is { } parentId) regions = regions.Where(r => r.ParentId == parentId);
        if (query.Type is { } type) regions = regions.Where(r => r.Type == type);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.ToLower();
            regions = regions.Where(r => r.NameUz.ToLower().Contains(search) || r.NameRu.ToLower().Contains(search));
        }

        return regions.OrderBy(r => r.NameUz)
            .Select(r => new RegionDto(r.Id, r.ParentId, r.Type, r.NameUz, r.NameRu, r.IsActive))
            .ToPagedAsync(query, ct);
    }

    public async Task<RegionDto> GetAsync(Guid id, CancellationToken ct) =>
        ToDto(await db.Regions.AsNoTracking().Where(r => r.Id == id).FirstOrNotFoundAsync(ct));

    public async Task<RegionDto> CreateAsync(SaveRegionRequest request, CancellationToken ct)
    {
        await EnsureValidParentAsync(request, ct);
        var region = new Region { ParentId = request.ParentId, Type = request.Type, NameUz = request.NameUz, NameRu = request.NameRu, IsActive = request.IsActive };
        db.Regions.Add(region);
        await db.SaveChangesAsync(ct);
        return ToDto(region);
    }

    public async Task<RegionDto> UpdateAsync(Guid id, SaveRegionRequest request, CancellationToken ct)
    {
        var region = await db.Regions.Where(r => r.Id == id).FirstOrNotFoundAsync(ct);
        await EnsureValidParentAsync(request, ct);

        region.ParentId = request.ParentId;
        region.Type = request.Type;
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

    private async Task EnsureValidParentAsync(SaveRegionRequest request, CancellationToken ct)
    {
        var valid = request.Type == RegionType.Province
            ? request.ParentId is null
            : request.ParentId is { } parentId && await db.Regions.AnyAsync(r => r.Id == parentId && r.Type == RegionType.Province, ct);
        if (!valid) throw new DomainException(ErrorCodes.RegionWrongParent);
    }

    private static RegionDto ToDto(Region r) => new(r.Id, r.ParentId, r.Type, r.NameUz, r.NameRu, r.IsActive);
}
