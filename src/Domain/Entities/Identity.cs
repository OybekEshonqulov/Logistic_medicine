using PharmaTrack.Domain.Common;
using PharmaTrack.Domain.Enums;

namespace PharmaTrack.Domain.Entities;

public sealed class User : Entity
{
    public required string FullName { get; set; }
    public required string Phone { get; set; }
    public string PasswordHash { get; set; } = string.Empty;
    public UserRole Role { get; set; }
    public Guid? DealerId { get; set; }
    public Dealer? Dealer { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class RefreshToken : Entity
{
    public Guid UserId { get; init; }
    public User User { get; init; } = null!;
    public required string TokenHash { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    public DateTimeOffset? RevokedAt { get; set; }
}
