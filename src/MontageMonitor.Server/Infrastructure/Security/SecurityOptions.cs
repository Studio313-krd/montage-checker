namespace MontageMonitor.Server.Infrastructure.Security;

public sealed class SecurityOptions
{
    public const string SectionName = "Security";

    public string JwtSigningKey { get; set; } = string.Empty;

    public string JwtIssuer { get; set; } = "MontageMonitor.Server";

    public string JwtAudience { get; set; } = "MontageMonitor.Web";

    public int AccessTokenMinutes { get; set; } = 15;

    public int RefreshTokenDays { get; set; } = 30;

    public bool SecureRefreshCookie { get; set; } = true;
}

public sealed class BootstrapOptions
{
    public const string SectionName = "Bootstrap";

    public string OwnerLogin { get; set; } = string.Empty;

    public string OwnerPassword { get; set; } = string.Empty;

    public string OwnerDisplayName { get; set; } = "Владелец";
}
