using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using MontageMonitor.Server.Domain;

namespace MontageMonitor.Server.Infrastructure.Security;

public sealed class TokenService(IOptions<SecurityOptions> options, TimeProvider timeProvider)
{
    public const string RefreshCookieName = "montage_refresh";

    private readonly SecurityOptions securityOptions = options.Value;

    public AccessTokenData CreateAccessToken(User user)
    {
        var now = timeProvider.GetUtcNow();
        var expiresAtUtc = now.AddMinutes(securityOptions.AccessTokenMinutes);
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Login),
            new Claim(ClaimTypes.Role, user.Role.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(securityOptions.JwtSigningKey)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: securityOptions.JwtIssuer,
            audience: securityOptions.JwtAudience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expiresAtUtc.UtcDateTime,
            signingCredentials: credentials);

        return new AccessTokenData(
            new JwtSecurityTokenHandler().WriteToken(token),
            expiresAtUtc);
    }

    public RefreshTokenData CreateRefreshToken(Guid userId)
    {
        var rawToken = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(64));
        var now = timeProvider.GetUtcNow();
        var entity = new RefreshToken
        {
            UserId = userId,
            TokenHash = HashRefreshToken(rawToken),
            ExpiresAtUtc = now.AddDays(securityOptions.RefreshTokenDays),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        return new RefreshTokenData(rawToken, entity);
    }

    public string HashRefreshToken(string rawToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));

    public void SetRefreshCookie(HttpResponse response, RefreshTokenData token)
    {
        response.Cookies.Append(RefreshCookieName, token.RawToken, CreateCookieOptions(token.Entity.ExpiresAtUtc));
    }

    public void DeleteRefreshCookie(HttpResponse response)
    {
        var options = CreateCookieOptions(timeProvider.GetUtcNow().AddDays(-1));
        response.Cookies.Delete(RefreshCookieName, options);
    }

    private CookieOptions CreateCookieOptions(DateTimeOffset expiresAtUtc) => new()
    {
        HttpOnly = true,
        Secure = securityOptions.SecureRefreshCookie,
        SameSite = SameSiteMode.Strict,
        IsEssential = true,
        Path = "/api/auth",
        Expires = expiresAtUtc,
    };
}

public sealed record AccessTokenData(string Token, DateTimeOffset ExpiresAtUtc);

public sealed record RefreshTokenData(string RawToken, RefreshToken Entity);
