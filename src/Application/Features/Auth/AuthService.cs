using FluentValidation;
using Microsoft.EntityFrameworkCore;
using PharmaTrack.Application.Common;
using PharmaTrack.Domain.Common;
using PharmaTrack.Domain.Entities;
using PharmaTrack.Domain.Enums;

namespace PharmaTrack.Application.Features.Auth;

public sealed record LoginRequest(string Phone, string Password);
public sealed record RefreshRequest(string RefreshToken);
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public sealed record MeDto(Guid Id, string FullName, string Phone, UserRole Role, Guid? DealerId, string? DealerName, IReadOnlyList<string> Permissions);
public sealed record AuthResponse(string AccessToken, string RefreshToken, int ExpiresIn, MeDto User);

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Phone).NotEmpty();
        RuleFor(x => x.Password).NotEmpty();
    }
}

public sealed class RefreshRequestValidator : AbstractValidator<RefreshRequest>
{
    public RefreshRequestValidator() => RuleFor(x => x.RefreshToken).NotEmpty();
}

public sealed class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty();
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(8);
    }
}

public sealed class AuthService(
    IAppDbContext db,
    IPasswordService passwords,
    ITokenService tokens,
    ICurrentUser currentUser,
    TimeProvider clock)
{
    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var user = await db.Users.Include(u => u.Dealer).FirstOrDefaultAsync(u => u.Phone == request.Phone, ct);
        if (user is null || !passwords.Verify(user.PasswordHash, request.Password))
            throw new DomainException(ErrorCodes.InvalidCredentials);
        if (!user.IsActive) throw new DomainException(ErrorCodes.UserInactive);

        return await IssueAsync(user, ct);
    }

    public async Task<AuthResponse> RefreshAsync(RefreshRequest request, CancellationToken ct)
    {
        var hash = tokens.HashRefreshToken(request.RefreshToken);
        var stored = await db.RefreshTokens
            .Include(t => t.User).ThenInclude(u => u.Dealer)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct)
            ?? throw new DomainException(ErrorCodes.InvalidRefreshToken);

        var now = clock.GetUtcNow();
        if (stored.RevokedAt is not null)
        {
            // A rotated token came back, so it may have been stolen: end every session of this user.
            await RevokeAllAsync(stored.UserId, ct);
            throw new DomainException(ErrorCodes.InvalidRefreshToken);
        }
        if (stored.ExpiresAt <= now) throw new DomainException(ErrorCodes.InvalidRefreshToken);
        if (!stored.User.IsActive) throw new DomainException(ErrorCodes.UserInactive);

        stored.RevokedAt = now;
        return await IssueAsync(stored.User, ct);
    }

    public async Task LogoutAsync(RefreshRequest request, CancellationToken ct)
    {
        var hash = tokens.HashRefreshToken(request.RefreshToken);
        var stored = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash && t.UserId == currentUser.UserId, ct);
        if (stored is null) return;

        stored.RevokedAt ??= clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
    }

    public async Task<MeDto> MeAsync(CancellationToken ct)
    {
        var user = await db.Users.Include(u => u.Dealer).Where(u => u.Id == currentUser.UserId).FirstOrNotFoundAsync(ct);
        return ToMe(user);
    }

    public async Task ChangePasswordAsync(ChangePasswordRequest request, CancellationToken ct)
    {
        var user = await db.Users.Where(u => u.Id == currentUser.UserId).FirstOrNotFoundAsync(ct);
        if (!passwords.Verify(user.PasswordHash, request.CurrentPassword))
            throw new DomainException(ErrorCodes.WrongPassword);

        user.PasswordHash = passwords.Hash(request.NewPassword);
        await RevokeAllAsync(user.Id, ct);
    }

    private async Task<AuthResponse> IssueAsync(User user, CancellationToken ct)
    {
        var issued = tokens.Issue(user);
        db.RefreshTokens.Add(new RefreshToken { UserId = user.Id, TokenHash = issued.RefreshTokenHash, ExpiresAt = issued.RefreshExpiresAt });
        await db.SaveChangesAsync(ct);
        return new AuthResponse(issued.AccessToken, issued.RefreshToken, issued.ExpiresInSeconds, ToMe(user));
    }

    private async Task RevokeAllAsync(Guid userId, CancellationToken ct)
    {
        var active = await db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null).ToListAsync(ct);
        var now = clock.GetUtcNow();
        active.ForEach(t => t.RevokedAt = now);
        await db.SaveChangesAsync(ct);
    }

    private static MeDto ToMe(User user) =>
        new(user.Id, user.FullName, user.Phone, user.Role, user.DealerId, user.Dealer?.Name, Permissions.For(user.Role));
}
