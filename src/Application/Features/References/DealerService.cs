using FluentValidation;
using Microsoft.EntityFrameworkCore;
using PharmaTrack.Application.Common;
using PharmaTrack.Domain.Entities;
using PharmaTrack.Domain.Enums;

namespace PharmaTrack.Application.Features.References;

public sealed record DealerDto(Guid Id, string Name, string Phone, Guid RegionId, string RegionName, string Address, bool IsActive, Guid LocationId, IReadOnlyList<Guid> ServiceRegionIds);
public sealed record SaveDealerRequest(string Name, string Phone, Guid RegionId, string Address, IReadOnlyList<Guid>? ServiceRegionIds, bool IsActive = true);
public sealed record DealerQuery : PageQuery
{
    public Guid? RegionId { get; init; }
    public bool? IsActive { get; init; }
}

public sealed class SaveDealerRequestValidator : AbstractValidator<SaveDealerRequest>
{
    public SaveDealerRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Phone).NotEmpty().Matches(ValidationPatterns.Phone);
        RuleFor(x => x.RegionId).NotEmpty();
        RuleFor(x => x.Address).NotEmpty().MaximumLength(500);
    }
}

public sealed class DealerService(IAppDbContext db, ICurrentUser currentUser)
{
    public Task<PagedResult<DealerDto>> ListAsync(DealerQuery query, CancellationToken ct)
    {
        var dealers = Scoped();
        if (query.RegionId is { } regionId)
            dealers = dealers.Where(d => d.RegionId == regionId || d.Region.ParentId == regionId);
        if (query.IsActive is { } isActive) dealers = dealers.Where(d => d.IsActive == isActive);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.ToLower();
            dealers = dealers.Where(d => d.Name.ToLower().Contains(search) || d.Phone.Contains(search));
        }

        return Project(dealers.OrderBy(d => d.Name)).ToPagedAsync(query, ct);
    }

    public async Task<DealerDto> GetAsync(Guid id, CancellationToken ct) =>
        await Project(Scoped().Where(d => d.Id == id)).FirstOrNotFoundAsync(ct);

    public async Task<DealerDto> CreateAsync(SaveDealerRequest request, CancellationToken ct)
    {
        await db.Regions.Where(r => r.Id == request.RegionId).FirstOrNotFoundAsync(ct);

        var dealer = new Dealer { Name = request.Name, Phone = request.Phone, RegionId = request.RegionId, Address = request.Address, IsActive = request.IsActive };
        ApplyServiceRegions(dealer, request);
        db.Dealers.Add(dealer);
        db.StockLocations.Add(new StockLocation { Type = LocationType.Dealer, DealerId = dealer.Id, Name = dealer.Name });
        await db.SaveChangesAsync(ct);
        return await GetAsync(dealer.Id, ct);
    }

    public async Task<DealerDto> UpdateAsync(Guid id, SaveDealerRequest request, CancellationToken ct)
    {
        var dealer = await db.Dealers.Include(d => d.ServiceRegions).Where(d => d.Id == id).FirstOrNotFoundAsync(ct);
        await db.Regions.Where(r => r.Id == request.RegionId).FirstOrNotFoundAsync(ct);

        dealer.Name = request.Name;
        dealer.Phone = request.Phone;
        dealer.RegionId = request.RegionId;
        dealer.Address = request.Address;
        dealer.IsActive = request.IsActive;
        dealer.ServiceRegions.Clear();
        ApplyServiceRegions(dealer, request);

        var location = await db.StockLocations.FirstAsync(l => l.DealerId == id, ct);
        location.Name = dealer.Name;
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task DeactivateAsync(Guid id, CancellationToken ct)
    {
        var dealer = await db.Dealers.Where(d => d.Id == id).FirstOrNotFoundAsync(ct);
        dealer.IsActive = false;
        await db.SaveChangesAsync(ct);
    }

    private IQueryable<Dealer> Scoped()
    {
        var dealers = db.Dealers.AsNoTracking();
        return currentUser.IsDealer() ? dealers.Where(d => d.Id == currentUser.DealerId) : dealers;
    }

    private static void ApplyServiceRegions(Dealer dealer, SaveDealerRequest request)
    {
        foreach (var regionId in (request.ServiceRegionIds ?? []).Distinct())
            dealer.ServiceRegions.Add(new DealerRegion { DealerId = dealer.Id, RegionId = regionId });
    }

    private IQueryable<DealerDto> Project(IQueryable<Dealer> dealers) => dealers.Select(d => new DealerDto(
        d.Id, d.Name, d.Phone, d.RegionId, d.Region.NameUz, d.Address, d.IsActive,
        db.StockLocations.Where(l => l.DealerId == d.Id).Select(l => l.Id).First(),
        d.ServiceRegions.Select(r => r.RegionId).ToList()));
}
