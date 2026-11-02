using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using PharmaTrack.Application.Common;
using PharmaTrack.Domain.Common;
using PharmaTrack.Domain.Enums;
using PharmaTrack.Infrastructure.Security;

namespace PharmaTrack.Api.Infrastructure;

public sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public Guid? UserIdOrNull =>
        Guid.TryParse(Principal?.FindFirstValue(JwtRegisteredClaimNames.Sub), out var id) ? id : null;

    public Guid UserId => UserIdOrNull ?? throw new DomainException(ErrorCodes.Unauthorized);

    public UserRole Role =>
        Enum.TryParse<UserRole>(Principal?.FindFirstValue(ClaimTypes.Role), out var role)
            ? role
            : throw new DomainException(ErrorCodes.Unauthorized);

    public Guid? DealerId =>
        Guid.TryParse(Principal?.FindFirstValue(AppClaims.DealerId), out var id) ? id : null;
}
