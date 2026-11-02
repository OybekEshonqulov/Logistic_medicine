using FluentValidation;
using Microsoft.EntityFrameworkCore;
using PharmaTrack.Application.Common;
using PharmaTrack.Domain.Common;
using PharmaTrack.Domain.Entities;
using PharmaTrack.Domain.Enums;

namespace PharmaTrack.Application.Features.Users;

public sealed record UserDto(Guid Id, string FullName, string Phone, UserRole Role, Guid? DealerId, string? DealerName, bool IsActive);
public sealed record CreateUserRequest(string FullName, string Phone, string Password, UserRole Role, Guid? DealerId);
public sealed record UpdateUserRequest(string FullName, string Phone, UserRole Role, Guid? DealerId, bool IsActive);
public sealed record ResetPasswordRequest(string NewPassword);
public sealed record UserQuery : PageQuery
{
    public UserRole? Role { get; init; }
    public Guid? DealerId { get; init; }
}

public sealed class CreateUserRequestValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserRequestValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Phone).NotEmpty().Matches(ValidationPatterns.Phone);
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8);
        RuleFor(x => x.Role).IsInEnum();
    }
}

public sealed class UpdateUserRequestValidator : AbstractValidator<UpdateUserRequest>
{
    public UpdateUserRequestValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Phone).NotEmpty().Matches(ValidationPatterns.Phone);
        RuleFor(x => x.Role).IsInEnum();
    }
}

public sealed class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator() => RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(8);
}

public sealed class UserService(IAppDbContext db, IPasswordService passwords)
{
    public Task<PagedResult<UserDto>> ListAsync(UserQuery query, CancellationToken ct)
    {
        var users = db.Users.AsNoTracking();
        if (query.Role is { } role) users = users.Where(u => u.Role == role);
        if (query.DealerId is { } dealerId) users = users.Where(u => u.DealerId == dealerId);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.ToLower();
            users = users.Where(u => u.FullName.ToLower().Contains(search) || u.Phone.Contains(search));
        }

        return users.OrderBy(u => u.FullName).Select(ToDto).ToPagedAsync(query, ct);
    }

    public async Task<UserDto> GetAsync(Guid id, CancellationToken ct) =>
        await db.Users.AsNoTracking().Where(u => u.Id == id).Select(ToDto).FirstOrNotFoundAsync(ct);

    public async Task<UserDto> CreateAsync(CreateUserRequest request, CancellationToken ct)
    {
        await EnsureValidAsync(request.Phone, request.Role, request.DealerId, null, ct);

        var user = new User
        {
            FullName = request.FullName,
            Phone = request.Phone,
            PasswordHash = passwords.Hash(request.Password),
            Role = request.Role,
            DealerId = request.Role == UserRole.Dealer ? request.DealerId : null
        };
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        return await GetAsync(user.Id, ct);
    }

    public async Task<UserDto> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken ct)
    {
        var user = await db.Users.Where(u => u.Id == id).FirstOrNotFoundAsync(ct);
        await EnsureValidAsync(request.Phone, request.Role, request.DealerId, id, ct);

        user.FullName = request.FullName;
        user.Phone = request.Phone;
        user.Role = request.Role;
        user.DealerId = request.Role == UserRole.Dealer ? request.DealerId : null;
        user.IsActive = request.IsActive;
        if (!user.IsActive) await RevokeSessionsAsync(id, ct);

        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task ResetPasswordAsync(Guid id, ResetPasswordRequest request, CancellationToken ct)
    {
        var user = await db.Users.Where(u => u.Id == id).FirstOrNotFoundAsync(ct);
        user.PasswordHash = passwords.Hash(request.NewPassword);
        await RevokeSessionsAsync(id, ct);
        await db.SaveChangesAsync(ct);
    }

    private async Task EnsureValidAsync(string phone, UserRole role, Guid? dealerId, Guid? excludeId, CancellationToken ct)
    {
        if (await db.Users.AnyAsync(u => u.Phone == phone && u.Id != excludeId, ct))
            throw new DomainException(ErrorCodes.PhoneTaken);

        if (role != UserRole.Dealer) return;
        if (dealerId is null || !await db.Dealers.AnyAsync(d => d.Id == dealerId, ct))
            throw new DomainException(ErrorCodes.DealerRequired);
    }

    private async Task RevokeSessionsAsync(Guid userId, CancellationToken ct) =>
        await db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, DateTimeOffset.UtcNow), ct);

    private static readonly System.Linq.Expressions.Expression<Func<User, UserDto>> ToDto =
        u => new UserDto(u.Id, u.FullName, u.Phone, u.Role, u.DealerId, u.Dealer != null ? u.Dealer.Name : null, u.IsActive);
}
