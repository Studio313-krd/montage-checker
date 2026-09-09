using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MontageMonitor.Server.Domain;
using MontageMonitor.Server.Infrastructure.Persistence;

namespace MontageMonitor.Server.Infrastructure.Security;

public static class AgentAuthenticationDefaults
{
    public const string Scheme = "AgentDevice";
    public const string AuthorizationScheme = "Device";
    public const string Policy = "agent.authenticated";
    public const string AgentIdClaim = "montage:agent_id";
    public const string EmployeeIdClaim = "montage:employee_id";
    public const string ComputerIdClaim = "montage:computer_id";
}

public sealed class AgentAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    MonitoringDbContext dbContext,
    AgentCredentialService credentialService,
    TimeProvider timeProvider)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var authorization = Request.Headers.Authorization.ToString();
        var prefix = AgentAuthenticationDefaults.AuthorizationScheme + " ";
        if (!authorization.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        var token = authorization[prefix.Length..].Trim();
        var separator = token.IndexOf('.', StringComparison.Ordinal);
        if (separator <= 0 || separator == token.Length - 1 ||
            !Guid.TryParseExact(token[..separator], "N", out var credentialId))
        {
            return AuthenticateResult.Fail("Некорректный device token.");
        }

        var data = await (
            from credential in dbContext.AgentCredentials.AsNoTracking()
            join agent in dbContext.Agents.AsNoTracking() on credential.AgentId equals agent.Id
            join computer in dbContext.Computers.AsNoTracking() on agent.ComputerId equals computer.Id
            where credential.Id == credentialId
            select new { credential, agent, computer })
            .SingleOrDefaultAsync(Context.RequestAborted);

        var now = timeProvider.GetUtcNow();
        if (data is null || data.credential.RevokedAtUtc is not null ||
            data.credential.ExpiresAtUtc <= now || data.agent.RevokedAtUtc is not null ||
            data.agent.Status == AgentStatus.Revoked || data.computer.IsRevoked ||
            !credentialService.Verify(data.credential, token[(separator + 1)..]))
        {
            return AuthenticateResult.Fail("Device token недействителен.");
        }

        var claims = new[]
        {
            new Claim(AgentAuthenticationDefaults.AgentIdClaim, data.agent.Id.ToString()),
            new Claim(AgentAuthenticationDefaults.EmployeeIdClaim, data.computer.EmployeeId.ToString()),
            new Claim(AgentAuthenticationDefaults.ComputerIdClaim, data.computer.Id.ToString()),
        };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Response.WriteAsJsonAsync(new { message = "Требуется действующий device token." });
    }
}

public static class AgentPrincipalExtensions
{
    public static Guid GetRequiredAgentId(this ClaimsPrincipal principal) =>
        GetRequiredGuid(principal, AgentAuthenticationDefaults.AgentIdClaim);

    public static Guid GetRequiredEmployeeId(this ClaimsPrincipal principal) =>
        GetRequiredGuid(principal, AgentAuthenticationDefaults.EmployeeIdClaim);

    public static Guid GetRequiredComputerId(this ClaimsPrincipal principal) =>
        GetRequiredGuid(principal, AgentAuthenticationDefaults.ComputerIdClaim);

    private static Guid GetRequiredGuid(ClaimsPrincipal principal, string claimType) =>
        Guid.TryParse(principal.FindFirstValue(claimType), out var value)
            ? value
            : throw new InvalidOperationException($"Device identity не содержит claim {claimType}.");
}
