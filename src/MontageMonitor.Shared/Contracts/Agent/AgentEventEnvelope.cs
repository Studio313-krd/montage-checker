using System.Text.Json;

namespace MontageMonitor.Shared.Contracts.Agent;

public sealed record AgentEventEnvelope(
    Guid EventId,
    Guid AgentId,
    DateTimeOffset OccurredAtUtc,
    string EventType,
    JsonElement Payload,
    int SchemaVersion = 1);
