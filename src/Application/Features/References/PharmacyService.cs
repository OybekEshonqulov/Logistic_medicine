using FluentValidation;
using Microsoft.EntityFrameworkCore;
using PharmaTrack.Application.Common;
using PharmaTrack.Domain.Common;
using PharmaTrack.Domain.Entities;
using PharmaTrack.Domain.Enums;

namespace PharmaTrack.Application.Features.References;

public sealed record PharmacyDto(Guid Id, string Name, Guid RegionId, string RegionName, Guid? DistrictId, string? DistrictName, string Address, string? Phone, PharmacyStatus Status, bool IsActive);
public sealed record SavePharmacyRequest(string Name, Guid RegionId, Guid? DistrictId, string Address, string? Phone = null, bool IsActive = true);
public sealed record PharmacyQuery : PageQuery
{
    public Guid? RegionId { get; init; }
    public Guid? DistrictId { get; init; }
    public PharmacyStatus? Status { get; init; }
}

public sealed class SavePharmacyRequestValidator : AbstractValidator<SavePharmacyRequest>
{
    public SavePharmacyRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.RegionId).NotEmpty();
        RuleFor(x => x.Address).NotEmpty().MaximumLength(500);
    }
}

public sealed class PharmacyService(IAppDbContext db, ICurrentUser currentUser, RegionService regions)
{
    public Task<PagedResult<PharmacyDto>> ListAsync(PharmacyQuery query, CancellationToken ct)
    {
        var pharmacies = Scoped();
        if (query.RegionId is { } regionId) pharmacies = pharmacies.Where(p => p.RegionId == regionId);
        if (query.DistrictId is { } districtId) pharmacies = pharmacies.Where(p => p.DistrictId == districtId);
        if (query.Status is { } status) pharmacies = pharmacies.Where(p => p.Status == status);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.ToLower();
            pharmacies = pharmacies.Where(p => p.Name.ToLower().Contains(search) || p.Address.ToLower().Contains(search));
        }

        return Project(pharmacies.OrderBy(p => p.Name)).ToPagedAsync(query, ct);
    }

    public async Task<PharmacyDto> GetAsync(Guid id, CancellationToken ct) =>
        await Project(Scoped().Where(p => p.Id == id)).FirstOrNotFoundAsync(ct);

    /// <summary>Central staff add a ready-to-use pharmacy; a dealer's addition waits for approval.</summary>
    public async Task<PharmacyDto> CreateAsync(SavePharmacyRequest request, CancellationToken ct)
    {
        await regions.EnsureValidAddressAsync(request.RegionId, request.DistrictId, ct);

        var pharmacy = new Pharmacy
        {
            Name = request.Name,
            RegionId = request.RegionId,
            DistrictId = request.DistrictId,
            Address = request.Address,
            Phone = request.Phone,
            IsActive = request.IsActive,
            Status = currentUser.IsDealer() ? PharmacyStatus.Pending : PharmacyStatus.Active,
            RequestedByDealerId = currentUser.DealerId
        };
        db.Pharmacies.Add(pharmacy);
        await db.SaveChangesAsync(ct);
        return await GetAsync(pharmacy.Id, ct);
    }

    public async Task<PharmacyDto> UpdateAsync(Guid id, SavePharmacyRequest request, CancellationToken ct)
    {
        var pharmacy = await db.Pharmacies.Where(p => p.Id == id).FirstOrNotFoundAsync(ct);
        await regions.EnsureValidAddressAsync(request.RegionId, request.DistrictId, ct);

        pharmacy.Name = request.Name;
        pharmacy.RegionId = request.RegionId;
        pharmacy.DistrictId = request.DistrictId;
        pharmacy.Address = request.Address;
        pharmacy.Phone = request.Phone;
        pharmacy.IsActive = request.IsActive;
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public Task<PharmacyDto> ApproveAsync(Guid id, CancellationToken ct) => ReviewAsync(id, PharmacyStatus.Active, ct);

    public Task<PharmacyDto> RejectAsync(Guid id, CancellationToken ct) => ReviewAsync(id, PharmacyStatus.Rejected, ct);

    public async Task DeactivateAsync(Guid id, CancellationToken ct)
    {
        var pharmacy = await db.Pharmacies.Where(p => p.Id == id).FirstOrNotFoundAsync(ct);
        pharmacy.IsActive = false;
        await db.SaveChangesAsync(ct);
    }

    private async Task<PharmacyDto> ReviewAsync(Guid id, PharmacyStatus status, CancellationToken ct)
    {
        var pharmacy = await db.Pharmacies.Where(p => p.Id == id).FirstOrNotFoundAsync(ct);
        if (pharmacy.Status != PharmacyStatus.Pending) throw new DomainException(ErrorCodes.PharmacyNotPending);

        pharmacy.Status = status;
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    private IQueryable<Pharmacy> Scoped()
    {
        var pharmacies = db.Pharmacies.AsNoTracking();
        if (!currentUser.IsDealer()) return pharmacies;

        var dealerId = currentUser.DealerId;
        return pharmacies.Where(p => p.Status == PharmacyStatus.Active || p.RequestedByDealerId == dealerId);
    }

    private static IQueryable<PharmacyDto> Project(IQueryable<Pharmacy> pharmacies) => pharmacies.Select(p => new PharmacyDto(
        p.Id, p.Name, p.RegionId, Languages.Pick(p.Region.NameUz, p.Region.NameRu, p.Region.NameCyrl), p.DistrictId, p.District != null ? Languages.Pick(p.District.NameUz, p.District.NameRu, p.District.NameCyrl) : null, p.Address, p.Phone, p.Status, p.IsActive));
}
