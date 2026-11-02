using Microsoft.EntityFrameworkCore;
using PharmaTrack.Application.Common;
using PharmaTrack.Domain.Common;
using PharmaTrack.Domain.Entities;
using PharmaTrack.Domain.Enums;

namespace PharmaTrack.Application.Features.Stock;

public sealed class LocationService(IAppDbContext db, ICurrentUser currentUser)
{
    public async Task<Guid> LocationOfDealerAsync(Guid dealerId, CancellationToken ct) =>
        await db.StockLocations.Where(l => l.DealerId == dealerId).Select(l => (Guid?)l.Id).FirstOrDefaultAsync(ct)
        ?? throw new DomainException(ErrorCodes.NotFound);

    public Task<Guid> MyLocationAsync(CancellationToken ct) =>
        LocationOfDealerAsync(currentUser.RequireDealerId(), ct);

    /// <summary>The location the caller works from when none is given: own stock for a dealer, the central store otherwise.</summary>
    public async Task<Guid> ResolveManagedLocationAsync(Guid? requested, CancellationToken ct)
    {
        if (currentUser.IsDealer())
        {
            var own = await MyLocationAsync(ct);
            if (requested is not null && requested != own) throw new DomainException(ErrorCodes.Forbidden);
            return own;
        }

        var locationId = requested ?? StockLocation.CentralId;
        if (currentUser.Role == UserRole.Warehouse && locationId != StockLocation.CentralId)
            throw new DomainException(ErrorCodes.Forbidden);
        if (!await db.StockLocations.AnyAsync(l => l.Id == locationId, ct))
            throw new DomainException(ErrorCodes.NotFound);
        return locationId;
    }

    public async Task EnsureCanReadAsync(Guid locationId, CancellationToken ct)
    {
        if (currentUser.IsDealer() && locationId != await MyLocationAsync(ct))
            throw new DomainException(ErrorCodes.Forbidden);
    }
}
