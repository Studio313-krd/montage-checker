using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MontageMonitor.Shared.Contracts.Agent;

namespace MontageMonitor.Agent.Configuration;

internal sealed class AgentSettingsStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("MontageMonitor.Agent/v1");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly object _gate = new();

    public AgentSettings? Load()
    {
        lock (_gate)
        {
            try
            {
                if (!File.Exists(AgentPaths.SettingsFile))
                {
                    return null;
                }

                var json = File.ReadAllText(AgentPaths.SettingsFile, Encoding.UTF8);
                var settings = JsonSerializer.Deserialize<AgentSettings>(json, JsonOptions);
                if (settings is null || settings.AgentId == Guid.Empty ||
                    settings.EmployeeId == Guid.Empty || settings.ComputerId == Guid.Empty ||
                    string.IsNullOrWhiteSpace(settings.ServerBaseUrl) ||
                    string.IsNullOrWhiteSpace(settings.ProtectedDeviceAccessToken))
                {
                    return null;
                }

                _ = GetDeviceAccessToken(settings);
                return settings;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or JsonException or
                FormatException or CryptographicException)
            {
                return null;
            }
        }
    }

    public AgentSettings SaveEnrollment(
        string serverBaseUrl,
        AgentEnrollmentResponse enrollment)
    {
        var protectedToken = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(enrollment.DeviceAccessToken),
            Entropy,
            DataProtectionScope.CurrentUser);
        var settings = new AgentSettings
        {
            ServerBaseUrl = serverBaseUrl.TrimEnd('/'),
            AgentId = enrollment.AgentId,
            EmployeeId = enrollment.EmployeeId,
            ComputerId = enrollment.ComputerId,
            ProtectedDeviceAccessToken = Convert.ToBase64String(protectedToken),
            CredentialExpiresAtUtc = enrollment.ExpiresAtUtc,
        };

        Save(settings);
        return settings;
    }

    public void SaveRemoteConfiguration(AgentSettings settings, AgentConfigurationResponse configuration)
    {
        settings.ConfigVersion = configuration.ConfigVersion;
        settings.HeartbeatIntervalSeconds = Math.Clamp(configuration.HeartbeatIntervalSeconds, 15, 300);
        settings.IdleThresholdSeconds = Math.Clamp(configuration.IdleThresholdSeconds, 0, 86_400);
        settings.RenderRules = configuration.RenderRules ?? [];
        settings.RenderFolders = configuration.RenderFolders ?? [];
        settings.ProxyRules = configuration.ProxyRules ?? [];
        settings.ProxyFolders = configuration.ProxyFolders ?? [];
        settings.Screenshots = configuration.Screenshots ?? settings.Screenshots;
        Save(settings);
    }

    public void SaveOperatorSession(AgentSettings settings, AgentOperatorSessionResponse session)
    {
        settings.OperatorSessionId = session.SessionId;
        settings.SelectedEmployeeId = session.EmployeeId;
        settings.SelectedEmployeeName = session.EmployeeName;
        settings.OperatorSessionExpiresAtUtc = session.ExpiresAtUtc;
        Save(settings);
    }

    public void ClearOperatorSession(AgentSettings settings)
    {
        settings.OperatorSessionId = null;
        settings.SelectedEmployeeId = null;
        settings.SelectedEmployeeName = null;
        settings.OperatorSessionExpiresAtUtc = null;
        Save(settings);
    }

    public string GetDeviceAccessToken(AgentSettings settings)
    {
        var protectedToken = Convert.FromBase64String(settings.ProtectedDeviceAccessToken);
        var token = ProtectedData.Unprotect(
            protectedToken,
            Entropy,
            DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(token);
    }

    private void Save(AgentSettings settings)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(AgentPaths.DataDirectory);
            var temporaryFile = AgentPaths.SettingsFile + ".tmp";
            File.WriteAllText(
                temporaryFile,
                JsonSerializer.Serialize(settings, JsonOptions),
                new UTF8Encoding(false));
            File.Move(temporaryFile, AgentPaths.SettingsFile, true);
        }
    }
}
