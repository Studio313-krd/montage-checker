using MontageMonitor.Server.Domain;
using MontageMonitor.Shared.States;

namespace MontageMonitor.Server.Features.Dashboard;

internal static class DashboardStateResolver
{
    public static bool IsOnline(
        AgentStatus? status,
        DateTimeOffset? lastSeenAtUtc,
        DateTimeOffset now,
        int offlineAfterSeconds) =>
        status == AgentStatus.Online &&
        lastSeenAtUtc.HasValue &&
        lastSeenAtUtc.Value >= now.AddSeconds(-offlineAfterSeconds);

    public static DateTimeOffset? PrimaryStateStartedAt(
        bool isOnline,
        MachineState machineState,
        DateTimeOffset? humanStartedAtUtc,
        DateTimeOffset? machineStartedAtUtc) =>
        isOnline && machineState != MachineState.Normal
            ? machineStartedAtUtc ?? humanStartedAtUtc
            : humanStartedAtUtc;
}
