using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using MontageMonitor.Server.Domain;
using MontageMonitor.Server.Features.Employees;
using MontageMonitor.Server.Infrastructure.Persistence;

namespace MontageMonitor.Server.Infrastructure.Security;

public static class SecurityExtensions
{
    public static IServiceCollection AddMontageSecurity(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection(SecurityOptions.SectionName);
        var securityOptions = section.Get<SecurityOptions>() ?? new SecurityOptions();
        if (Encoding.UTF8.GetByteCount(securityOptions.JwtSigningKey) < 32)
        {
            throw new InvalidOperationException(
                "Security:JwtSigningKey должен содержать не менее 32 байт.");
        }

        services.AddOptions<SecurityOptions>()
            .Bind(section)
            .Validate(
                options => Encoding.UTF8.GetByteCount(options.JwtSigningKey) >= 32,
                "Security:JwtSigningKey должен содержать не менее 32 байт.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.JwtIssuer),
                "Security:JwtIssuer обязателен.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.JwtAudience),
                "Security:JwtAudience обязателен.")
            .Validate(
                options => options.AccessTokenMinutes is >= 5 and <= 60,
                "Security:AccessTokenMinutes должен быть от 5 до 60.")
            .Validate(
                options => options.RefreshTokenDays is >= 1 and <= 90,
                "Security:RefreshTokenDays должен быть от 1 до 90.")
            .ValidateOnStart();
        services.Configure<BootstrapOptions>(
            configuration.GetSection(BootstrapOptions.SectionName));
        services.Configure<PasswordHasherOptions>(options =>
            options.IterationCount = 210_000);

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
        services.AddScoped<TokenService>();
        services.AddScoped<AgentCredentialService>();
        services.AddScoped<EmployeeAgentPasswordService>();
        services.AddScoped<AuditWriter>();
        services.AddScoped<EmployeeAccessService>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = securityOptions.JwtIssuer,
                    ValidateAudience = true,
                    ValidAudience = securityOptions.JwtAudience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(securityOptions.JwtSigningKey)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = ClaimTypes.Name,
                    RoleClaimType = ClaimTypes.Role,
                };
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = async context =>
                    {
                        var principal = context.Principal;
                        if (principal is null || !principal.TryGetUserId(out var userId))
                        {
                            context.Fail("JWT не содержит идентификатор пользователя.");
                            return;
                        }

                        var dbContext = context.HttpContext.RequestServices
                            .GetRequiredService<MonitoringDbContext>();
                        var user = await dbContext.Users.AsNoTracking()
                            .SingleOrDefaultAsync(item => item.Id == userId, context.HttpContext.RequestAborted);
                        if (user is null || !user.IsActive || !principal.IsInRole(user.Role.ToString()))
                        {
                            context.Fail("Учётная запись недоступна или её роль изменилась.");
                        }
                    },
                    OnChallenge = async context =>
                    {
                        context.HandleResponse();
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        await context.Response.WriteAsJsonAsync(new
                        {
                            message = "Требуется авторизация.",
                        });
                    },
                    OnForbidden = context =>
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        return context.Response.WriteAsJsonAsync(new
                        {
                            message = "Недостаточно прав для выполнения операции.",
                        });
                    },
                };
            })
            .AddScheme<AuthenticationSchemeOptions, AgentAuthenticationHandler>(
                AgentAuthenticationDefaults.Scheme,
                _ => { });

        services.AddAuthorizationBuilder()
            .AddPolicy(SecurityPolicies.ViewEmployees, policy =>
                policy.RequireRole(SecurityPolicies.AllRoles))
            .AddPolicy(SecurityPolicies.ManageEmployees, policy =>
                policy.RequireRole(SecurityPolicies.AdministrativeRoles))
            .AddPolicy(SecurityPolicies.ViewScreenshots, policy =>
                policy.RequireRole(SecurityPolicies.ManagementViewRoles))
            .AddPolicy(SecurityPolicies.DownloadReports, policy =>
                policy.RequireRole(SecurityPolicies.ManagementViewRoles))
            .AddPolicy(SecurityPolicies.ManageUsers, policy =>
                policy.RequireRole(SecurityPolicies.AdministrativeRoles))
            .AddPolicy(AgentAuthenticationDefaults.Policy, policy =>
            {
                policy.AddAuthenticationSchemes(AgentAuthenticationDefaults.Scheme);
                policy.RequireAuthenticatedUser();
            });

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.ContentType = "application/problem+json";
                await context.HttpContext.Response.WriteAsJsonAsync(new
                {
                    title = "Слишком много запросов",
                    status = StatusCodes.Status429TooManyRequests,
                    detail = "Повторите попытку через одну минуту.",
                }, cancellationToken);
            };
            options.AddPolicy(SecurityPolicies.LoginRateLimit, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        AutoReplenishment = true,
                        PermitLimit = 5,
                        QueueLimit = 0,
                        Window = TimeSpan.FromMinutes(1),
                    }));
            options.AddPolicy(SecurityPolicies.AgentLoginRateLimit, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        AutoReplenishment = true,
                        PermitLimit = 5,
                        QueueLimit = 0,
                        Window = TimeSpan.FromMinutes(1),
                    }));
            options.AddPolicy(SecurityPolicies.OperatorPasswordRateLimit, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        AutoReplenishment = true,
                        PermitLimit = 10,
                        QueueLimit = 0,
                        Window = TimeSpan.FromMinutes(1),
                    }));
            options.AddPolicy(SecurityPolicies.ScreenshotUploadRateLimit, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        AutoReplenishment = true,
                        PermitLimit = 120,
                        QueueLimit = 0,
                        Window = TimeSpan.FromMinutes(1),
                    }));
        });

        return services;
    }
}
