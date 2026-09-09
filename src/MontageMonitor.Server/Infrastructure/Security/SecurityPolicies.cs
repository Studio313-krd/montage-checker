using MontageMonitor.Server.Domain;

namespace MontageMonitor.Server.Infrastructure.Security;

public static class SecurityPolicies
{
    public const string ViewEmployees = "employees.view";
    public const string ManageEmployees = "employees.manage";
    public const string ViewScreenshots = "screenshots.view";
    public const string DownloadReports = "reports.download";
    public const string ManageUsers = "users.manage";
    public const string LoginRateLimit = "auth.login";
    public const string EnrollmentRateLimit = "agent.enrollment";
    public const string OperatorPinRateLimit = "agent.operator-pin";
    public const string ScreenshotUploadRateLimit = "agent.screenshot-upload";

    public static readonly string[] AllRoles =
    [
        nameof(UserRole.Owner),
        nameof(UserRole.Admin),
        nameof(UserRole.Manager),
        nameof(UserRole.Viewer),
    ];

    public static readonly string[] AdministrativeRoles =
    [
        nameof(UserRole.Owner),
        nameof(UserRole.Admin),
    ];

    public static readonly string[] ManagementViewRoles =
    [
        nameof(UserRole.Owner),
        nameof(UserRole.Admin),
        nameof(UserRole.Manager),
    ];
}
