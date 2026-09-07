using MontageMonitor.Shared.Contracts.Agent;

namespace MontageMonitor.Agent.Configuration;

internal sealed class AgentSettings
{
    public required string ServerBaseUrl { get; set; }

    public Guid AgentId { get; set; }

    public Guid EmployeeId { get; set; }

    public Guid ComputerId { get; set; }

    public required string ProtectedDeviceAccessToken { get; set; }

    public DateTimeOffset CredentialExpiresAtUtc { get; set; }

    public long ConfigVersion { get; set; }

    public int HeartbeatIntervalSeconds { get; set; } = 30;

    public int IdleThresholdSeconds { get; set; } = 300;

    public IReadOnlyList<AgentRenderRule> RenderRules { get; set; } = [];

    public IReadOnlyList<AgentWatchedFolder> RenderFolders { get; set; } = [];

    public IReadOnlyList<AgentProxyRule> ProxyRules { get; set; } = [];

    public IReadOnlyList<AgentWatchedFolder> ProxyFolders { get; set; } = [];

    public AgentScreenshotConfiguration Screenshots { get; set; } = new(
        false,
        5,
        ScreenshotCaptureMode.PrimaryMonitor,
        1_600,
        60,
        []);
}
