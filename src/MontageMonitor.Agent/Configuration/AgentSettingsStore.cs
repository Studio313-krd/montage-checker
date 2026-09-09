using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MontageMonitor.Shared.Contracts.Agent;

namespace MontageMonitor.Agent.Configuration;

internal sealed class AgentSettingsStore
{
    private const int CurrentAuthenticationSchemaVersion = 1;
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
                    string.IsNullOrWhiteSpace(settings.ProtectedDeviceAccessToken))
                {
                    return null;
                }

                _ = GetDeviceAccessToken(settings);
                var changed = false;
                if (settings.AuthenticationSchemaVersion < CurrentAuthenticationSchemaVersion)
                {
                    ClearOperatorSessionValues(settings);
                    settings.AuthenticationSchemaVersion = CurrentAuthenticationSchemaVersion;
                    changed = true;
                }

                if (changed)
                {
                    Save(settings);
                }

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

    public AgentSettings SaveLogin(AgentLoginResponse login)
    {
        var protectedToken = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(login.DeviceAccessToken),
            Entropy,
            DataProtectionScope.CurrentUser);
        var settings = new AgentSettings
        {
            AgentId = login.AgentId,
            EmployeeId = login.EmployeeId,
            ComputerId = login.ComputerId,
            ProtectedDeviceAccessToken = Convert.ToBase64String(protectedToken),
            CredentialExpiresAtUtc = login.ExpiresAtUtc,
            AuthenticationSchemaVersion = CurrentAuthenticationSchemaVersion,
        };

        SaveOperatorSessionValues(settings, login.OperatorSession);
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
        SaveOperatorSessionValues(settings, session);
        Save(settings);
    }

    public void ClearOperatorSession(AgentSettings settings)
    {
        ClearOperatorSessionValues(settings);
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

    private static void SaveOperatorSessionValues(
        AgentSettings settings,
        AgentOperatorSessionResponse session)
    {
        settings.OperatorSessionId = session.SessionId;
        settings.SelectedEmployeeId = session.EmployeeId;
        settings.SelectedEmployeeName = session.EmployeeName;
        settings.OperatorSessionExpiresAtUtc = session.ExpiresAtUtc;
    }

    private static void ClearOperatorSessionValues(AgentSettings settings)
    {
        settings.OperatorSessionId = null;
        settings.SelectedEmployeeId = null;
        settings.SelectedEmployeeName = null;
        settings.OperatorSessionExpiresAtUtc = null;
    }
}
