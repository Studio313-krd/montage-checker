using System.Net;
using System.Text;
using MontageMonitor.Agent.Abstractions;
using MontageMonitor.Agent.Networking;
using MontageMonitor.Shared.Contracts.Agent;
using MontageMonitor.Shared.States;
using Xunit;

namespace MontageMonitor.Agent.Tests;

public sealed class AgentApiClientTests
{
    [Theory]
    [InlineData(400, (int)HeartbeatSendResult.Rejected)]
    [InlineData(401, (int)HeartbeatSendResult.Unauthorized)]
    [InlineData(428, (int)HeartbeatSendResult.OperatorSelectionRequired)]
    [InlineData(500, (int)HeartbeatSendResult.RetryLater)]
    public async Task FailedHeartbeat_ReportsHttpStatusWithoutLoggingResponsePayload(int statusCode, int expected)
    {
        using var client = CreateClient(new HttpResponseMessage((HttpStatusCode)statusCode)
        {
            Content = new StringContent(
                """{"errors":{"TimestampUtc":["private-response-value"]}}""",
                Encoding.UTF8, "application/problem+json"),
        });

        var result = await client.SendHeartbeatAsync(Heartbeat(), TestContext.Current.CancellationToken);

        Assert.Equal((HeartbeatSendResult)expected, result);
        Assert.Contains($"HTTP {statusCode}", client.LastErrorDetails);
        Assert.DoesNotContain("private-response-value", client.LastErrorDetails);
        if (statusCode == 400)
        {
            Assert.Contains("TimestampUtc", client.LastErrorDetails);
        }
    }

    [Fact]
    public async Task RejectedHeartbeat_ReportsLargeClockDifference()
    {
        var response = new HttpResponseMessage((HttpStatusCode)428);
        response.Headers.Date = DateTimeOffset.UtcNow.AddHours(-2);
        using var client = CreateClient(response);

        await client.SendHeartbeatAsync(Heartbeat(), TestContext.Current.CancellationToken);

        Assert.Contains("Расхождение часов ПК и сервера", client.LastErrorDetails);
    }

    [Fact]
    public async Task SuccessfulHeartbeat_ClearsPreviousFailure()
    {
        using var client = CreateClient(
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            new HttpResponseMessage(HttpStatusCode.OK));

        await client.SendHeartbeatAsync(Heartbeat(), TestContext.Current.CancellationToken);
        Assert.NotNull(client.LastErrorDetails);
        var result = await client.SendHeartbeatAsync(Heartbeat(), TestContext.Current.CancellationToken);

        Assert.Equal(HeartbeatSendResult.Sent, result);
        Assert.Null(client.LastErrorDetails);
    }

    private static AgentApiClient CreateClient(params HttpResponseMessage[] responses) =>
        new(new HttpClient(new ResponseHandler(responses)) { BaseAddress = new Uri("https://agent-test.invalid/") });

    private static HeartbeatRequest Heartbeat() => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "0.10.2",
        DateTimeOffset.UtcNow, "test", "test", HumanState.Active, MachineState.Normal,
        null, null, null, 0, 0, 0, OperatorSessionId: Guid.NewGuid());

    private sealed class ResponseHandler(IEnumerable<HttpResponseMessage> responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_responses.Dequeue());
    }
}
