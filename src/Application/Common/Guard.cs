using Microsoft.EntityFrameworkCore;
using PharmaTrack.Domain.Common;
using PharmaTrack.Domain.Enums;

namespace PharmaTrack.Application.Common;

public static class Guard
{
    public static async Task<T> FirstOrNotFoundAsync<T>(this IQueryable<T> query, CancellationToken ct) where T : class =>
        await query.FirstOrDefaultAsync(ct) ?? throw new DomainException(ErrorCodes.NotFound);

    public static Guid RequireDealerId(this ICurrentUser user) =>
        user.DealerId ?? throw new DomainException(ErrorCodes.Forbidden);

    public static bool IsDealer(this ICurrentUser user) => user.Role == UserRole.Dealer;

    public static bool IsCentralStaff(this ICurrentUser user) => user.Role is UserRole.Admin or UserRole.Warehouse;
}
