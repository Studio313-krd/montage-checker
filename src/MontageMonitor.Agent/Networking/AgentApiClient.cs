using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MontageMonitor.Agent.Abstractions;
using MontageMonitor.Agent.Configuration;
using MontageMonitor.Shared.Contracts.Agent;

namespace MontageMonitor.Agent.Networking;

internal sealed class AgentApiClient : IAgentApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly HttpClient _httpClient;

    public AgentApiClient(AgentSettings settings, string deviceAccessToken)
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Brotli,
        };
        _httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(settings.ServerBaseUrl + "/", UriKind.Absolute),
            Timeout = TimeSpan.FromSeconds(15),
        };
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Device", deviceAccessToken);
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"MontageMonitor.Agent/{AgentEnvironment.Version}");
    }

    public async Task<HeartbeatSendResult> SendHeartbeatAsync(
        HeartbeatRequest heartbeat,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.PostAsJsonAsync(
                "api/agent/heartbeat",
                heartbeat,
                JsonOptions,
                cancellationToken);
            return response.StatusCode switch
            {
                HttpStatusCode.OK => HeartbeatSendResult.Sent,
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => HeartbeatSendResult.Unauthorized,
                (HttpStatusCode)428 => HeartbeatSendResult.OperatorSelectionRequired,
                HttpStatusCode.BadRequest => HeartbeatSendResult.Rejected,
                _ when (int)response.StatusCode >= 500 => HeartbeatSendResult.RetryLater,
                _ => HeartbeatSendResult.RetryLater,
            };
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return HeartbeatSendResult.RetryLater;
        }
    }

    public async Task<AgentConfigurationResponse?> GetConfigurationAsync(
        Guid? operatorSessionId,
        CancellationToken cancellationToken)
    {
        try
        {
            var path = operatorSessionId.HasValue
                ? $"api/agent/config?operatorSessionId={operatorSessionId.Value:D}"
                : "api/agent/config";
            using var response = await _httpClient.GetAsync(path, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return await response.Content.ReadFromJsonAsync<AgentConfigurationResponse>(
                JsonOptions,
                cancellationToken);
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    public async Task<AgentOperatorOptionsResponse?> GetOperatorOptionsAsync(
        Guid? operatorSessionId,
        CancellationToken cancellationToken)
    {
        try
        {
            var path = operatorSessionId.HasValue
                ? $"api/agent/operators?operatorSessionId={operatorSessionId.Value:D}"
                : "api/agent/operators";
            using var response = await _httpClient.GetAsync(path, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return await response.Content.ReadFromJsonAsync<AgentOperatorOptionsResponse>(
                JsonOptions,
                cancellationToken);
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    public async Task<AgentOperatorSessionResponse> StartOperatorSessionAsync(
        Guid employeeId,
        string pin,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            "api/agent/operator-session",
            new StartAgentOperatorSessionRequest(employeeId, pin),
            JsonOptions,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var message = response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => "Неверный PIN-код.",
                HttpStatusCode.TooManyRequests => "Слишком много попыток. Подождите одну минуту.",
                _ => $"Сервер отклонил выбор монтажёра: HTTP {(int)response.StatusCode}.",
            };
            throw new OperatorSelectionException(message);
        }

        return await response.Content.ReadFromJsonAsync<AgentOperatorSessionResponse>(
                   JsonOptions,
                   cancellationToken)
               ?? throw new OperatorSelectionException("Сервер вернул пустой ответ.");
    }

    public async Task<HeartbeatSendResult> UploadScreenshotAsync(
        QueuedScreenshot screenshot,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(screenshot.LocalFilePath))
            {
                return HeartbeatSendResult.Rejected;
            }

            await using var fileStream = new FileStream(
                screenshot.LocalFilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1_024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var multipart = new MultipartFormDataContent();
            multipart.Add(
                new StringContent(JsonSerializer.Serialize(screenshot.Metadata, JsonOptions)),
                "metadata");
            using var imageContent = new StreamContent(fileStream);
            imageContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            multipart.Add(imageContent, "file", screenshot.EventId.ToString("N") + ".jpg");
            using var response = await _httpClient.PostAsync(
                "api/agent/screenshots",
                multipart,
                cancellationToken);
            return response.StatusCode switch
            {
                HttpStatusCode.OK or HttpStatusCode.Created => HeartbeatSendResult.Sent,
                HttpStatusCode.Unauthorized => HeartbeatSendResult.Unauthorized,
                (HttpStatusCode)428 => HeartbeatSendResult.OperatorSelectionRequired,
                HttpStatusCode.BadRequest or HttpStatusCode.Forbidden or
                    HttpStatusCode.RequestEntityTooLarge or
                    HttpStatusCode.UnsupportedMediaType => HeartbeatSendResult.Rejected,
                _ => HeartbeatSendResult.RetryLater,
            };
        }
        catch (Exception exception) when (
            exception is HttpRequestException or IOException or
            TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return HeartbeatSendResult.RetryLater;
        }
    }

    public void Dispose() => _httpClient.Dispose();

    public static async Task<AgentEnrollmentResponse> EnrollAsync(
        Uri serverBaseUri,
        string enrollmentToken,
        CancellationToken cancellationToken)
    {
        using var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Brotli,
        };
        using var client = new HttpClient(handler)
        {
            BaseAddress = serverBaseUri,
            Timeout = TimeSpan.FromSeconds(20),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"MontageMonitor.Agent/{AgentEnvironment.Version}");
        var request = new AgentEnrollmentRequest(
            enrollmentToken.Trim(),
            Environment.MachineName,
            AgentEnvironment.WindowsUser,
            System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            AgentEnvironment.Version);
        using var response = await client.PostAsJsonAsync(
            "api/agent/enroll",
            request,
            JsonOptions,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var message = response.StatusCode == HttpStatusCode.Unauthorized
                ? "Код регистрации недействителен, уже использован или истёк."
                : $"Сервер отклонил регистрацию: HTTP {(int)response.StatusCode}.";
            throw new AgentEnrollmentException(message);
        }

        return await response.Content.ReadFromJsonAsync<AgentEnrollmentResponse>(JsonOptions, cancellationToken)
            ?? throw new AgentEnrollmentException("Сервер вернул пустой ответ регистрации.");
    }
}

internal sealed class AgentEnrollmentException(string message) : Exception(message);

internal sealed class OperatorSelectionException(string message) : Exception(message);
