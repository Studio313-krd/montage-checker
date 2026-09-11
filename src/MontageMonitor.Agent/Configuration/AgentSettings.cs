using MontageMonitor.Shared.Contracts.Agent;
using System.Text.Json.Serialization;

namespace MontageMonitor.Agent.Configuration;

internal sealed class AgentSettings
{
    public Guid AgentId { get; set; }

    public Guid EmployeeId { get; set; }

    public Guid ComputerId { get; set; }

    public required string ProtectedDeviceAccessToken { get; set; }

    public DateTimeOffset CredentialExpiresAtUtc { get; set; }

    public Guid? OperatorSessionId { get; set; }

    public Guid? SelectedEmployeeId { get; set; }

    public string? SelectedEmployeeName { get; set; }

    public DateTimeOffset? OperatorSessionExpiresAtUtc { get; set; }

    public long ServerClockOffsetTicks { get; set; }

    [JsonIgnore]
    public ServerClock Clock { get; set; } = new();

    public int AuthenticationSchemaVersion { get; set; }

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

    public bool HasValidOperatorSession(DateTimeOffset nowUtc) =>
        OperatorSessionId.HasValue &&
        SelectedEmployeeId.HasValue &&
        OperatorSessionExpiresAtUtc > nowUtc;
}
