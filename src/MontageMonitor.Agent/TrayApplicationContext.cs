using System.Drawing;
using MontageMonitor.Agent.Activity;
using MontageMonitor.Agent.Configuration;
using MontageMonitor.Agent.Idle;
using MontageMonitor.Agent.Lifecycle;
using MontageMonitor.Agent.Metrics;
using MontageMonitor.Agent.Networking;
using MontageMonitor.Agent.Processes;
using MontageMonitor.Agent.Proxy;
using MontageMonitor.Agent.Rendering;
using MontageMonitor.Agent.Screenshots;
using MontageMonitor.Agent.Storage;

namespace MontageMonitor.Agent;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly AgentSettingsStore _settingsStore = new();
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _detailsItem;
    private readonly ToolStripMenuItem _lastSyncItem;
    private readonly ToolStripMenuItem _queueItem;
    private readonly ToolStripMenuItem _machineStateItem;
    private readonly ToolStripMenuItem _operatorItem;
    private readonly ToolStripMenuItem _startupItem;
    private readonly ToolStripMenuItem _switchOperatorItem;
    private readonly ToolStripMenuItem _configureItem;
    private readonly NotifyIcon _notifyIcon;
    private readonly System.Windows.Forms.Timer _startupTimer;
    private readonly System.Windows.Forms.Timer _operatorTimer;
    private SynchronizationContext? _uiContext;
    private AgentRuntime? _runtime;
    private AgentSettings? _settings;
    private bool _configurationDialogOpen;
    private bool _operatorSelectionOpen;
    private bool _exitInProgress;

    public TrayApplicationContext()
    {
        _statusItem = DisabledItem("Статус: запуск");
        _detailsItem = DisabledItem("Причина: —");
        _detailsItem.Visible = false;
        _lastSyncItem = DisabledItem("Последняя синхронизация: —");
        _queueItem = DisabledItem("В очереди: 0");
        _machineStateItem = DisabledItem("Машинная работа: норма");
        _operatorItem = DisabledItem("Монтажёр: не выбран");
        _startupItem = DisabledItem("Автозапуск: настройка…");
        _configureItem = new ToolStripMenuItem("Войти заново…");
        _configureItem.Click += async (_, _) => await ConfigureAsync();
        _switchOperatorItem = new ToolStripMenuItem("Сменить монтажёра…");
        _switchOperatorItem.Click += async (_, _) => await EnsureOperatorSelectionAsync(force: true);

        var exitItem = new ToolStripMenuItem("Выход");
        exitItem.Click += async (_, _) => await RequestExitAsync();
        var diagnosticsItem = new ToolStripMenuItem("Открыть журнал диагностики…");
        diagnosticsItem.Click += (_, _) => OpenDiagnostics();

        _menu = new ContextMenuStrip();
        _menu.Items.AddRange([
            _statusItem,
            _detailsItem,
            _lastSyncItem,
            _queueItem,
            _machineStateItem,
            _operatorItem,
            _startupItem,
            new ToolStripSeparator(),
            _switchOperatorItem,
            _configureItem,
            diagnosticsItem,
            new ToolStripSeparator(),
            exitItem,
        ]);

        _notifyIcon = new NotifyIcon
        {
            ContextMenuStrip = _menu,
            Icon = AppBranding.Icon,
            Text = "MontageMonitor работает",
            Visible = true,
        };
        _notifyIcon.MouseClick += (_, eventArgs) =>
        {
            if (eventArgs.Button == MouseButtons.Left)
            {
                _menu.Show(Cursor.Position);
            }
        };
        _notifyIcon.DoubleClick += async (_, _) =>
        {
            if (_runtime is null)
            {
                if (_settings is null)
                {
                    await ConfigureAsync();
                }
                else
                {
                    await EnsureOperatorSelectionAsync(force: true);
                }
            }
        };

        _startupTimer = new System.Windows.Forms.Timer { Interval = 150 };
        _startupTimer.Tick += StartAsync;
        _startupTimer.Start();

        _operatorTimer = new System.Windows.Forms.Timer { Interval = 30_000 };
        _operatorTimer.Tick += async (_, _) =>
        {
            if (_settings is not null && !_settings.HasValidOperatorSession(_settings.Clock.GetUtcNow()))
            {
                await EnsureOperatorSelectionAsync();
            }
        };
        _operatorTimer.Start();
    }

    private static ToolStripMenuItem DisabledItem(string text) => new(text) { Enabled = false };

    private async void StartAsync(object? sender, EventArgs eventArgs)
    {
        _startupTimer.Stop();
        _uiContext = SynchronizationContext.Current;
        _ = RegisterAutostartAsync();

        _settings = _settingsStore.Load();
        if (_settings is null)
        {
            SetUnconfiguredStatus();
            await ConfigureAsync();
            return;
        }

        try
        {
            using var apiClient = new AgentApiClient(_settingsStore.GetDeviceAccessToken(_settings));
            var options = await apiClient.GetOperatorOptionsAsync(CancellationToken.None);
            _settingsStore.SaveServerTime(_settings, options.ServerTimeUtc);
            LogClockSynchronization(_settings);
        }
        catch (AgentAuthenticationRequiredException)
        {
            await ConfigureAsync();
            return;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or OperatorSelectionException)
        {
            AgentDiagnosticLog.Write($"Clock synchronization unavailable: {exception.GetType().Name}; using saved offset.");
        }

        if (_settings.HasValidOperatorSession(_settings.Clock.GetUtcNow()))
        {
            StartRuntime(_settings);
        }
        else
        {
            await EnsureOperatorSelectionAsync();
        }
    }

    private async Task RegisterAutostartAsync()
    {
        var result = await Task.Run(TaskSchedulerRegistrar.Register);
        PostToUi(() =>
        {
            _startupItem.Text = result.Succeeded
                ? "Автозапуск: включён"
                : "Автозапуск: ошибка настройки";
            if (!result.Succeeded)
            {
                _notifyIcon.BalloonTipTitle = "MontageMonitor";
                _notifyIcon.BalloonTipText =
                    "Не удалось включить автозапуск. Обратитесь к администратору.";
                _notifyIcon.BalloonTipIcon = ToolTipIcon.Warning;
                _notifyIcon.ShowBalloonTip(5_000);
            }
        });
    }

    private async Task ConfigureAsync()
    {
        if (_configurationDialogOpen)
        {
            return;
        }

        _configurationDialogOpen = true;
        _configureItem.Enabled = false;
        try
        {
            var previousSettings = _settings;
            await StopRuntimeAsync();
            using var dialog = new AgentSetupDialog();
            if (dialog.ShowDialog() != DialogResult.OK ||
                dialog.LoginResult is null)
            {
                if (previousSettings is not null)
                {
                    StartRuntime(previousSettings);
                }
                else
                {
                    SetUnconfiguredStatus();
                }

                return;
            }

            var newSettings = _settingsStore.SaveLogin(dialog.LoginResult);
            LogClockSynchronization(newSettings);
            if (previousSettings is not null &&
                (previousSettings.AgentId != newSettings.AgentId ||
                 previousSettings.EmployeeId != newSettings.EmployeeId ||
                 previousSettings.ComputerId != newSettings.ComputerId))
            {
                var queue = new SqliteHeartbeatQueue();
                await queue.InitializeAsync(CancellationToken.None);
                await queue.ClearAsync(CancellationToken.None);
                var screenshotQueue = new SqliteScreenshotQueue();
                await screenshotQueue.InitializeAsync(CancellationToken.None);
                await screenshotQueue.ClearAsync(CancellationToken.None);
            }

            _settings = newSettings;
            _operatorItem.Text = $"Монтажёр: {newSettings.SelectedEmployeeName}";
            StartRuntime(newSettings);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        {
            MessageBox.Show(
                "Не удалось сохранить настройки Agent. Проверьте права профиля Windows.",
                "MontageMonitor",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            SetUnconfiguredStatus();
        }
        finally
        {
            _configurationDialogOpen = false;
            _configureItem.Enabled = true;
        }
    }

    private async Task EnsureOperatorSelectionAsync(bool force = false, string? reason = null)
    {
        if (_operatorSelectionOpen || _settings is null)
        {
            return;
        }

        if (!force && _settings.HasValidOperatorSession(_settings.Clock.GetUtcNow()))
        {
            if (_runtime is null)
            {
                StartRuntime(_settings);
            }

            return;
        }

        _operatorSelectionOpen = true;
        _switchOperatorItem.Enabled = false;
        await StopRuntimeAsync();
        _settingsStore.ClearOperatorSession(_settings);
        _operatorItem.Text = "Монтажёр: требуется выбор";
        var authenticationRequired = false;
        try
        {
            var deviceAccessToken = _settingsStore.GetDeviceAccessToken(_settings);
            using var apiClient = new AgentApiClient(deviceAccessToken);
            using var dialog = new OperatorSelectionDialog(apiClient, reason);
            _ = dialog.ShowDialog();
            if (dialog.AuthenticationRequired)
            {
                authenticationRequired = true;
            }
            else if (dialog.ExitRequested)
            {
                Environment.ExitCode = 0;
                ExitThread();
                return;
            }
            else if (dialog.Session is null)
            {
                return;
            }
            else
            {
                _settingsStore.SaveOperatorSession(_settings, dialog.Session);
                LogClockSynchronization(_settings);
                _operatorItem.Text = $"Монтажёр: {dialog.Session.EmployeeName}";
                StartRuntime(_settings);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
            System.Security.Cryptography.CryptographicException or UriFormatException)
        {
            MessageBox.Show(
                "Не удалось сохранить выбранного монтажёра. Обратитесь к администратору.",
                "MontageMonitor",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            _operatorSelectionOpen = false;
            if (!_menu.IsDisposed)
            {
                _switchOperatorItem.Enabled = _settings is not null;
            }
        }

        if (authenticationRequired)
        {
            await ConfigureAsync();
        }
    }

    private void StartRuntime(AgentSettings settings)
    {
        if (!settings.HasValidOperatorSession(settings.Clock.GetUtcNow()))
        {
            _ = EnsureOperatorSelectionAsync();
            return;
        }

        try
        {
            _operatorItem.Text = $"Монтажёр: {settings.SelectedEmployeeName}";
            var deviceAccessToken = _settingsStore.GetDeviceAccessToken(settings);
            _runtime = new AgentRuntime(
                settings,
                _settingsStore,
                new WindowsActivityMonitor(),
                new WindowsIdleMonitor(),
                new WindowsSystemMetricsProvider(),
                new RenderDetector(
                    new WindowsProcessMonitor(),
                    new WindowsWatchedFolderMonitor(),
                    new NvidiaSmiGpuMetricsProvider()),
                new ProxyDetector(
                    new WindowsProcessMonitor(),
                    new WindowsWatchedFolderMonitor()),
                new WindowsScreenshotCapture(),
                new SqliteHeartbeatQueue(),
                new SqliteScreenshotQueue(),
                new AgentApiClient(deviceAccessToken));
            _runtime.StatusChanged += RuntimeStatusChanged;
            _runtime.FatalError += RuntimeFatalError;
            _runtime.Start();
        }
        catch (Exception exception) when (
            exception is UriFormatException or FormatException or
            System.Security.Cryptography.CryptographicException)
        {
            _runtime = null;
            SetUnconfiguredStatus();
        }
    }

    private void RuntimeStatusChanged(object? sender, AgentRuntimeStatus status) =>
        PostToUi(() =>
        {
            if (!ReferenceEquals(sender, _runtime))
            {
                return;
            }

            ApplyStatus(status);
            if (status.State == AgentConnectionState.OperatorSelectionRequired)
            {
                _ = EnsureOperatorSelectionAsync(reason: status.Details);
            }
            else if (status.State == AgentConnectionState.AuthenticationRequired)
            {
                _ = ConfigureAsync();
            }
        });

    private void RuntimeFatalError(object? sender, Exception exception) =>
        PostToUi(() =>
        {
            AgentDiagnosticLog.Failure(exception);
            Environment.ExitCode = 1;
            ExitThread();
        });

    private void ApplyStatus(AgentRuntimeStatus status)
    {
        AgentDiagnosticLog.Status(status);
        _detailsItem.Text = status.Details ?? "Причина: —";
        _detailsItem.Visible = !string.IsNullOrWhiteSpace(status.Details);
        _statusItem.Text = status.State switch
        {
            AgentConnectionState.Starting => "Статус: запуск",
            AgentConnectionState.Connected => "Статус: подключено",
            AgentConnectionState.Offline => "Статус: нет связи, данные сохранены",
            AgentConnectionState.AuthenticationRequired => "Статус: требуется повторный вход",
            AgentConnectionState.OperatorSelectionRequired => "Статус: выберите монтажёра",
            _ => "Статус: неизвестно",
        };
        _lastSyncItem.Text = status.LastSyncAtUtc is null
            ? "Последняя синхронизация: —"
            : $"Последняя синхронизация: {status.LastSyncAtUtc.Value.ToLocalTime():dd.MM.yyyy HH:mm:ss}";
        _queueItem.Text = $"В очереди: {status.QueuedCount}";
        _machineStateItem.Text = status.MachineState switch
        {
            MontageMonitor.Shared.States.MachineState.Render => "Машинная работа: рендер",
            MontageMonitor.Shared.States.MachineState.Proxy => "Машинная работа: proxy",
            MontageMonitor.Shared.States.MachineState.BackgroundProcessing => "Машинная работа: фоновые задачи",
            _ => "Машинная работа: норма",
        };
        _notifyIcon.Text = status.State switch
        {
            AgentConnectionState.Connected => "MontageMonitor — подключено",
            AgentConnectionState.Offline => "MontageMonitor — нет связи",
            AgentConnectionState.AuthenticationRequired => "MontageMonitor — требуется вход",
            AgentConnectionState.OperatorSelectionRequired => "MontageMonitor — выберите монтажёра",
            _ => "MontageMonitor работает",
        };
    }

    private void SetUnconfiguredStatus()
    {
        _detailsItem.Visible = false;
        _statusItem.Text = "Статус: требуется настройка";
        _lastSyncItem.Text = "Последняя синхронизация: —";
        _queueItem.Text = "В очереди: —";
        _machineStateItem.Text = "Машинная работа: —";
        _operatorItem.Text = "Монтажёр: не выбран";
        _notifyIcon.Text = "MontageMonitor — требуется настройка";
    }

    private static void LogClockSynchronization(AgentSettings settings) =>
        AgentDiagnosticLog.Write(
            $"Server clock synchronized; offsetSeconds={TimeSpan.FromTicks(settings.ServerClockOffsetTicks).TotalSeconds:F3}; " +
            $"serverUtc={settings.Clock.GetUtcNow():O}; shiftExpiresUtc={settings.OperatorSessionExpiresAtUtc:O}");

    private static void OpenDiagnostics()
    {
        try
        {
            AgentDiagnosticLog.Write($"Diagnostic log opened; Agent {AgentEnvironment.Version}");
            var startInfo = new System.Diagnostics.ProcessStartInfo("notepad.exe")
            {
                UseShellExecute = false,
            };
            startInfo.ArgumentList.Add(AgentDiagnosticLog.FilePath);
            using var process = System.Diagnostics.Process.Start(startInfo);
        }
        catch (Exception exception) when (exception is IOException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show($"Журнал: {AgentDiagnosticLog.FilePath}", "MontageMonitor");
        }
    }

    private void PostToUi(Action action)
    {
        if (_uiContext is null)
        {
            return;
        }

        _uiContext.Post(_ => action(), null);
    }

    private async Task StopRuntimeAsync()
    {
        if (_runtime is null)
        {
            return;
        }

        _runtime.StatusChanged -= RuntimeStatusChanged;
        _runtime.FatalError -= RuntimeFatalError;
        await _runtime.DisposeAsync();
        _runtime = null;
    }

    private async Task RequestExitAsync()
    {
        if (_exitInProgress)
        {
            return;
        }

        using var dialog = new ShutdownDialog();
        if (dialog.ShowDialog() != DialogResult.OK)
        {
            return;
        }

        _exitInProgress = true;
        _startupTimer.Stop();
        _operatorTimer.Stop();
        _menu.Enabled = false;
        _notifyIcon.Text = "MontageMonitor — завершение работы";
        await StopRuntimeAsync();
        Environment.ExitCode = 0;
        ExitThread();
    }

    protected override void ExitThreadCore()
    {
        if (_runtime is not null)
        {
            _runtime.StatusChanged -= RuntimeStatusChanged;
            _runtime.FatalError -= RuntimeFatalError;
            _runtime.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _runtime = null;
        }

        _startupTimer.Dispose();
        _operatorTimer.Dispose();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
        base.ExitThreadCore();
    }
}
