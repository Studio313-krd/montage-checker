using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MontageMonitor.Server.Domain;
using MontageMonitor.Server.Infrastructure.Persistence;
using MontageMonitor.Server.Infrastructure.Security;

namespace MontageMonitor.Server.Features.Auth;

public static class AuthEndpoints
{
    private const int FailedLoginLimit = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/auth")
            .WithTags("Авторизация");

        group.MapPost("/login", LoginAsync)
            .AllowAnonymous()
            .RequireRateLimiting(SecurityPolicies.LoginRateLimit)
            .WithName("Login")
            .WithSummary("Войти в систему");
        group.MapPost("/refresh", RefreshAsync)
            .AllowAnonymous()
            .WithName("RefreshToken")
            .WithSummary("Обновить access-токен");
        group.MapPost("/logout", LogoutAsync)
            .AllowAnonymous()
            .WithName("Logout")
            .WithSummary("Завершить сеанс");
        group.MapGet("/me", GetCurrentUserAsync)
            .RequireAuthorization()
            .WithName("GetCurrentUser")
            .WithSummary("Получить текущего пользователя");

        return endpoints;
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        HttpContext httpContext,
        MonitoringDbContext dbContext,
        IPasswordHasher<User> passwordHasher,
        TokenService tokenService,
        AuditWriter auditWriter,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Login) || string.IsNullOrEmpty(request.Password))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.Login)] = ["Укажите логин."],
                [nameof(request.Password)] = ["Укажите пароль."],
            });
        }

        var normalizedLogin = OwnerBootstrapper.NormalizeLogin(request.Login);
        var user = await dbContext.Users.SingleOrDefaultAsync(
            item => item.NormalizedLogin == normalizedLogin,
            cancellationToken);
        var now = timeProvider.GetUtcNow();

        PasswordVerificationResult verification;
        if (user is null)
        {
            var dummyUser = CreateDummyUser(normalizedLogin, now);
            _ = passwordHasher.HashPassword(dummyUser, request.Password);
            verification = PasswordVerificationResult.Failed;
        }
        else
        {
            verification = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        }

        var isLocked = user?.LockoutEndUtc > now;
        if (user is null || !user.IsActive || isLocked || verification == PasswordVerificationResult.Failed)
        {
            var reason = GetFailureReason(user, isLocked);
            if (user is not null && user.IsActive && !isLocked && verification == PasswordVerificationResult.Failed)
            {
                if (user.LockoutEndUtc is not null && user.LockoutEndUtc <= now)
                {
                    user.FailedLoginCount = 0;
                    user.LockoutEndUtc = null;
                }

                user.FailedLoginCount++;
                if (user.FailedLoginCount >= FailedLoginLimit)
                {
                    user.LockoutEndUtc = now.Add(LockoutDuration);
                }

                user.UpdatedAtUtc = now;
            }

            auditWriter.Add(
                httpContext,
                "auth.login.failed",
                "Authentication",
                user?.Id.ToString(),
                user?.Id,
                new { login = request.Login.Trim(), reason });
            await dbContext.SaveChangesAsync(cancellationToken);
            return InvalidCredentials();
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = passwordHasher.HashPassword(user, request.Password);
        }

        user.FailedLoginCount = 0;
        user.LockoutEndUtc = null;
        user.LastLoginAtUtc = now;
        user.UpdatedAtUtc = now;

        var refreshToken = tokenService.CreateRefreshToken(user.Id);
        dbContext.RefreshTokens.Add(refreshToken.Entity);
        auditWriter.Add(
            httpContext,
            "auth.login.succeeded",
            "Authentication",
            user.Id.ToString(),
            user.Id);
        await dbContext.SaveChangesAsync(cancellationToken);

        tokenService.SetRefreshCookie(httpContext.Response, refreshToken);
        return Results.Ok(CreateTokenResponse(tokenService, user));
    }

    private static async Task<IResult> RefreshAsync(
        HttpContext httpContext,
        MonitoringDbContext dbContext,
        TokenService tokenService,
        AuditWriter auditWriter,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!httpContext.Request.Cookies.TryGetValue(TokenService.RefreshCookieName, out var rawToken) ||
            string.IsNullOrWhiteSpace(rawToken))
        {
            tokenService.DeleteRefreshCookie(httpContext.Response);
            return InvalidRefreshToken();
        }

        var tokenHash = tokenService.HashRefreshToken(rawToken);
        var currentToken = await dbContext.RefreshTokens
            .SingleOrDefaultAsync(item => item.TokenHash == tokenHash, cancellationToken);
        if (currentToken is null)
        {
            auditWriter.Add(httpContext, "auth.refresh.failed", "Authentication", details: new { reason = "unknown_token" });
            await dbContext.SaveChangesAsync(cancellationToken);
            tokenService.DeleteRefreshCookie(httpContext.Response);
            return InvalidRefreshToken();
        }

        var user = await dbContext.Users.SingleOrDefaultAsync(
            item => item.Id == currentToken.UserId,
            cancellationToken);
        var now = timeProvider.GetUtcNow();
        if (user is null || !user.IsActive || currentToken.ExpiresAtUtc <= now)
        {
            await RevokeTokenAsync(dbContext, currentToken.Id, now, cancellationToken);
            auditWriter.Add(
                httpContext,
                "auth.refresh.failed",
                "Authentication",
                currentToken.Id.ToString(),
                user?.Id,
                new { reason = user is null || !user.IsActive ? "inactive_user" : "expired_token" });
            await dbContext.SaveChangesAsync(cancellationToken);
            tokenService.DeleteRefreshCookie(httpContext.Response);
            return InvalidRefreshToken();
        }

        if (currentToken.RevokedAtUtc is not null)
        {
            await RevokeAllTokensAsync(dbContext, user.Id, now, cancellationToken);
            auditWriter.Add(
                httpContext,
                "auth.refresh.reuse_detected",
                "Authentication",
                currentToken.Id.ToString(),
                user.Id);
            await dbContext.SaveChangesAsync(cancellationToken);
            tokenService.DeleteRefreshCookie(httpContext.Response);
            return InvalidRefreshToken();
        }

        var nextToken = tokenService.CreateRefreshToken(user.Id);
        currentToken.RevokedAtUtc = now;
        currentToken.ReplacedByTokenId = nextToken.Entity.Id;
        currentToken.UpdatedAtUtc = now;
        dbContext.RefreshTokens.Add(nextToken.Entity);
        auditWriter.Add(
            httpContext,
            "auth.refresh.succeeded",
            "Authentication",
            nextToken.Entity.Id.ToString(),
            user.Id);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            dbContext.ChangeTracker.Clear();
            await RevokeAllTokensAsync(dbContext, user.Id, now, cancellationToken);
            auditWriter.Add(
                httpContext,
                "auth.refresh.reuse_detected",
                "Authentication",
                currentToken.Id.ToString(),
                user.Id);
            await dbContext.SaveChangesAsync(cancellationToken);
            tokenService.DeleteRefreshCookie(httpContext.Response);
            return InvalidRefreshToken();
        }

        tokenService.SetRefreshCookie(httpContext.Response, nextToken);
        return Results.Ok(CreateTokenResponse(tokenService, user));
    }

    private static async Task<IResult> LogoutAsync(
        HttpContext httpContext,
        MonitoringDbContext dbContext,
        TokenService tokenService,
        AuditWriter auditWriter,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (httpContext.Request.Cookies.TryGetValue(TokenService.RefreshCookieName, out var rawToken) &&
            !string.IsNullOrWhiteSpace(rawToken))
        {
            var tokenHash = tokenService.HashRefreshToken(rawToken);
            var token = await dbContext.RefreshTokens.AsNoTracking()
                .SingleOrDefaultAsync(item => item.TokenHash == tokenHash, cancellationToken);
            if (token is not null)
            {
                var now = timeProvider.GetUtcNow();
                await RevokeTokenAsync(dbContext, token.Id, now, cancellationToken);
                auditWriter.Add(
                    httpContext,
                    "auth.logout",
                    "Authentication",
                    token.Id.ToString(),
                    token.UserId);
                await dbContext.SaveChangesAsync(cancellationToken);
            }
        }

        tokenService.DeleteRefreshCookie(httpContext.Response);
        return Results.NoContent();
    }

    private static async Task<IResult> GetCurrentUserAsync(
        HttpContext httpContext,
        MonitoringDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var userId = httpContext.User.GetRequiredUserId();
        var user = await dbContext.Users.AsNoTracking()
            .SingleAsync(item => item.Id == userId, cancellationToken);
        return Results.Ok(ToAuthUser(user));
    }

    private static Task<int> RevokeTokenAsync(
        MonitoringDbContext dbContext,
        Guid tokenId,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        dbContext.RefreshTokens
            .Where(item => item.Id == tokenId && item.RevokedAtUtc == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.RevokedAtUtc, now)
                .SetProperty(item => item.UpdatedAtUtc, now), cancellationToken);

    private static Task<int> RevokeAllTokensAsync(
        MonitoringDbContext dbContext,
        Guid userId,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        dbContext.RefreshTokens
            .Where(item => item.UserId == userId && item.RevokedAtUtc == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.RevokedAtUtc, now)
                .SetProperty(item => item.UpdatedAtUtc, now), cancellationToken);

    private static AccessTokenResponse CreateTokenResponse(TokenService tokenService, User user)
    {
        var token = tokenService.CreateAccessToken(user);
        return new AccessTokenResponse(
            token.Token,
            "Bearer",
            token.ExpiresAtUtc,
            ToAuthUser(user));
    }

    private static AuthUserResponse ToAuthUser(User user) =>
        new(user.Id, user.Login, user.DisplayName, user.Role);

    private static User CreateDummyUser(string login, DateTimeOffset now) => new()
    {
        Login = login,
        NormalizedLogin = login,
        DisplayName = "Проверка",
        PasswordHash = string.Empty,
        Role = UserRole.Viewer,
        IsActive = false,
        CreatedAtUtc = now,
        UpdatedAtUtc = now,
    };

    private static string GetFailureReason(User? user, bool isLocked) =>
        user switch
        {
            null => "unknown_user",
            { IsActive: false } => "inactive_user",
            _ when isLocked => "locked_out",
            _ => "invalid_password",
        };

    private static IResult InvalidCredentials() => Results.Json(
        new { message = "Неверный логин или пароль." },
        statusCode: StatusCodes.Status401Unauthorized);

    private static IResult InvalidRefreshToken() => Results.Json(
        new { message = "Сеанс истёк. Войдите в систему снова." },
        statusCode: StatusCodes.Status401Unauthorized);
}
