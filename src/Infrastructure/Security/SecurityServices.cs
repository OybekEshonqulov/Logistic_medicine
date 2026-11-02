using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PharmaTrack.Application.Common;
using PharmaTrack.Domain.Entities;

namespace PharmaTrack.Infrastructure.Security;

public sealed class JwtOptions
{
    public const string Section = "Jwt";

    public string Issuer { get; init; } = "PharmaTrack";
    public string Audience { get; init; } = "PharmaTrack";
    public string SigningKey { get; init; } = string.Empty;
    public int AccessTokenMinutes { get; init; } = 30;
    public int RefreshTokenDays { get; init; } = 30;
}

public static class AppClaims
{
    public const string DealerId = "dealer_id";
}

public sealed class TokenService(IOptions<JwtOptions> options, TimeProvider clock) : ITokenService
{
    private readonly JwtOptions _options = options.Value;

    public IssuedTokens Issue(User user)
    {
        var now = clock.GetUtcNow();
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(ClaimTypes.Role, user.Role.ToString())
        };
        if (user.DealerId is { } dealerId) claims.Add(new Claim(AppClaims.DealerId, dealerId.ToString()));

        var credentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey)), SecurityAlgorithms.HmacSha256);
        var jwt = new JwtSecurityToken(
            _options.Issuer,
            _options.Audience,
            claims,
            now.UtcDateTime,
            now.AddMinutes(_options.AccessTokenMinutes).UtcDateTime,
            credentials);

        var refreshToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        return new IssuedTokens(
            new JwtSecurityTokenHandler().WriteToken(jwt),
            refreshToken,
            HashRefreshToken(refreshToken),
            _options.AccessTokenMinutes * 60,
            now.AddDays(_options.RefreshTokenDays));
    }

    public string HashRefreshToken(string refreshToken) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)));
}

public sealed class PasswordService : IPasswordService
{
    private static readonly User Nobody = new() { FullName = string.Empty, Phone = string.Empty };
    private readonly PasswordHasher<User> _hasher = new();

    public string Hash(string password) => _hasher.HashPassword(Nobody, password);

    public bool Verify(string hash, string password) =>
        hash.Length > 0 && _hasher.VerifyHashedPassword(Nobody, hash, password) != PasswordVerificationResult.Failed;
}
