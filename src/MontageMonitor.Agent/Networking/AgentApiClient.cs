using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MontageMonitor.Agent.Abstractions;
using MontageMonitor.Shared.Contracts.Agent;

namespace MontageMonitor.Agent.Networking;

internal sealed class AgentApiClient : IAgentApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly HttpClient _httpClient;

    public string? LastErrorDetails { get; private set; }

    public AgentApiClient(string deviceAccessToken)
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Brotli,
        };
        _httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(AgentEnvironment.ServerBaseUrl + "/", UriKind.Absolute),
            Timeout = TimeSpan.FromSeconds(15),
        };
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Device", deviceAccessToken);
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"MontageMonitor.Agent/{AgentEnvironment.Version}");
    }

    internal AgentApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
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
            LastErrorDetails = await DescribeResponseAsync(response, "Heartbeat", cancellationToken);
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
            LastErrorDetails = DescribeNetworkFailure(exception, "Heartbeat");
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

    public async Task<AgentOperatorOptionsResponse> GetOperatorOptionsAsync(
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync("api/agent/operators", cancellationToken);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new AgentAuthenticationRequiredException(
                "Подключение компьютера отозвано или истекло. Войдите заново.");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new OperatorSelectionException(
                $"Не удалось получить список сотрудников: HTTP {(int)response.StatusCode}.");
        }

        return await response.Content.ReadFromJsonAsync<AgentOperatorOptionsResponse>(
                   JsonOptions,
                   cancellationToken)
               ?? throw new OperatorSelectionException("Сервер вернул пустой список сотрудников.");
    }

    public async Task<AgentOperatorSessionResponse> StartOperatorSessionAsync(
        Guid employeeId,
        string password,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            "api/agent/operator-session",
            new StartAgentOperatorSessionRequest(null, password, employeeId),
            JsonOptions,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                if (responseBody.Contains("device token", StringComparison.OrdinalIgnoreCase))
                {
                    throw new AgentAuthenticationRequiredException(
                        "Подключение компьютера отозвано или истекло. Войдите заново.");
                }
            }

            var message = response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => "Неверный сотрудник или пароль.",
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
                LastErrorDetails = "Скриншот: локальный файл очереди отсутствует.";
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
            LastErrorDetails = await DescribeResponseAsync(response, "Скриншот", cancellationToken);
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
            LastErrorDetails = DescribeNetworkFailure(exception, "Скриншот");
            return HeartbeatSendResult.RetryLater;
        }
    }

    private static async Task<string?> DescribeResponseAsync(
        HttpResponseMessage response,
        string operation,
        CancellationToken cancellationToken)
    {
        if (response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created)
        {
            return null;
        }

        var reason = response.StatusCode switch
        {
            HttpStatusCode.BadRequest => "сервер отклонил данные",
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "доступ отклонён",
            (HttpStatusCode)428 => "сервер не подтвердил смену для времени события",
            HttpStatusCode.TooManyRequests => "слишком много запросов",
            _ => "запрос не принят сервером",
        };
        var details = $"{operation}: HTTP {(int)response.StatusCode}, {reason}.";
        if (response.StatusCode == HttpStatusCode.BadRequest &&
            response.Content.Headers.ContentType?.MediaType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true)
        {
            try
            {
                using var document = await JsonDocument.ParseAsync(
                    await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
                if (document.RootElement.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
                {
                    // Record field names only; never copy payloads, credentials or arbitrary error bodies.
                    var fields = errors.EnumerateObject().Take(8)
                        .Select(item => new string(item.Name.Where(char.IsLetterOrDigit).Take(64).ToArray()));
                    details += " Поля: " + string.Join(", ", fields) + ".";
                }
            }
            catch (JsonException)
            {
                // The HTTP status still explains a malformed error response.
            }
        }

        if (response.Headers.Date is { } serverDate)
        {
            var clockDifference = DateTimeOffset.UtcNow - serverDate;
            if (Math.Abs(clockDifference.TotalSeconds) > 30)
            {
                details += $" Расхождение часов ПК и сервера: {clockDifference.TotalSeconds:F0} с. Проверьте время Windows.";
            }
        }

        return details;
    }

    private static string DescribeNetworkFailure(Exception exception, string operation) => exception switch
    {
        TaskCanceledException => $"{operation}: сервер не ответил за отведённое время.",
        HttpRequestException request => $"{operation}: ошибка соединения ({request.HttpRequestError}).",
        _ => $"{operation}: ошибка чтения файла ({exception.GetType().Name}).",
    };

    public void Dispose() => _httpClient.Dispose();

    public static async Task<AgentLoginOptionsResponse> GetLoginOptionsAsync(
        CancellationToken cancellationToken)
    {
        using var client = CreateAnonymousClient();
        using var response = await client.GetAsync("api/agent/login-options", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var message = response.StatusCode switch
            {
                HttpStatusCode.TooManyRequests =>
                    "Слишком много запросов. Подождите одну минуту и обновите список.",
                _ => $"Не удалось получить список сотрудников: HTTP {(int)response.StatusCode}.",
            };
            throw new AgentLoginException(message);
        }

        return await response.Content.ReadFromJsonAsync<AgentLoginOptionsResponse>(
                   JsonOptions,
                   cancellationToken)
               ?? throw new AgentLoginException("Сервер вернул пустой список сотрудников.");
    }

    public static async Task<AgentLoginResponse> LoginAsync(
        Guid employeeId,
        string password,
        CancellationToken cancellationToken)
    {
        using var client = CreateAnonymousClient();
        var request = new AgentLoginRequest(
            null,
            password,
            Environment.MachineName,
            AgentEnvironment.WindowsUser,
            System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            AgentEnvironment.Version,
            employeeId);
        using var response = await client.PostAsJsonAsync(
            "api/agent/login",
            request,
            JsonOptions,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var message = response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => "Неверный сотрудник или пароль.",
                HttpStatusCode.TooManyRequests => "Слишком много попыток. Подождите одну минуту.",
                _ => $"Сервер отклонил вход: HTTP {(int)response.StatusCode}.",
            };
            throw new AgentLoginException(message);
        }

        return await response.Content.ReadFromJsonAsync<AgentLoginResponse>(JsonOptions, cancellationToken)
            ?? throw new AgentLoginException("Сервер вернул пустой ответ входа.");
    }

    private static HttpClient CreateAnonymousClient()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Brotli,
        };
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri(AgentEnvironment.ServerBaseUrl + "/", UriKind.Absolute),
            Timeout = TimeSpan.FromSeconds(20),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"MontageMonitor.Agent/{AgentEnvironment.Version}");
        return client;
    }
}

internal sealed class AgentLoginException(string message) : Exception(message);

internal sealed class OperatorSelectionException(string message) : Exception(message);

internal sealed class AgentAuthenticationRequiredException(string message) : Exception(message);
