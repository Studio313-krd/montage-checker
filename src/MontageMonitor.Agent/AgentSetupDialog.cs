using System.Drawing;
using MontageMonitor.Agent.Networking;
using MontageMonitor.Shared.Contracts.Agent;

namespace MontageMonitor.Agent;

internal sealed class AgentSetupDialog : Form
{
    private static readonly Color Graphite = Color.FromArgb(21, 25, 31);
    private static readonly Color Slate = Color.FromArgb(80, 89, 103);
    private static readonly Color Canvas = Color.FromArgb(242, 243, 245);
    private static readonly Color Amber = Color.FromArgb(244, 185, 66);
    private static readonly Color Success = Color.FromArgb(47, 125, 97);
    private static readonly Color Error = Color.FromArgb(185, 68, 68);

    private readonly TextBox _serverTextBox;
    private readonly TextBox _tokenTextBox;
    private readonly Button _connectButton;
    private readonly Button _closeButton;
    private readonly Label _statusLabel;
    private readonly CancellationTokenSource _cancellation = new();

    public AgentSetupDialog(string? currentServerUrl = null)
    {
        Text = "Настройка MontageMonitor";
        Icon = AppBranding.Icon;
        ClientSize = new Size(570, 440);
        MinimumSize = new Size(570, 440);
        MaximumSize = new Size(760, 560);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Canvas;
        ForeColor = Graphite;
        Font = new Font("Segoe UI", 9.5f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        AutoScaleMode = AutoScaleMode.Dpi;

        var rootLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0),
            Padding = new Padding(0),
        };
        rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 126));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(rootLayout);

        var header = CreateHeader();
        rootLayout.Controls.Add(header, 0, 0);

        var content = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(34, 24, 34, 24),
            Margin = new Padding(0),
        };
        rootLayout.Controls.Add(content, 0, 1);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 8,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 39));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 12));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 39));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        content.Controls.Add(layout);

        layout.Controls.Add(CreateFieldLabel("Адрес сервера"), 0, 0);
        _serverTextBox = CreateTextBox();
        _serverTextBox.Text = currentServerUrl ?? "https://monitor.company.ru";
        layout.Controls.Add(_serverTextBox, 0, 1);

        layout.Controls.Add(CreateFieldLabel("Одноразовый код регистрации"), 0, 3);
        _tokenTextBox = CreateTextBox();
        _tokenTextBox.UseSystemPasswordChar = true;
        layout.Controls.Add(_tokenTextBox, 0, 4);

        _statusLabel = new Label
        {
            Text = "Код выдаёт администратор для конкретного сотрудника.",
            Dock = DockStyle.Fill,
            ForeColor = Slate,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
        };
        layout.Controls.Add(_statusLabel, 0, 5);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0),
        };
        _connectButton = new Button
        {
            Text = "Подключить Agent",
            AutoSize = false,
            Size = new Size(158, 38),
            BackColor = Graphite,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
        };
        _connectButton.FlatAppearance.BorderSize = 0;
        _connectButton.Click += ConnectAsync;
        _closeButton = new Button
        {
            Text = "Закрыть",
            AutoSize = false,
            Size = new Size(100, 38),
            FlatStyle = FlatStyle.Flat,
            ForeColor = Graphite,
            Cursor = Cursors.Hand,
            Margin = new Padding(0, 0, 10, 0),
        };
        _closeButton.FlatAppearance.BorderColor = Color.FromArgb(195, 199, 205);
        _closeButton.Click += (_, _) => Close();
        buttons.Controls.Add(_connectButton);
        buttons.Controls.Add(_closeButton);
        layout.Controls.Add(buttons, 0, 7);

        AcceptButton = _connectButton;
        CancelButton = _closeButton;
    }

    public AgentEnrollmentResponse? Enrollment { get; private set; }

    public string? ServerBaseUrl { get; private set; }

    protected override void OnFormClosed(FormClosedEventArgs eventArgs)
    {
        _cancellation.Cancel();
        _cancellation.Dispose();
        base.OnFormClosed(eventArgs);
    }

    private async void ConnectAsync(object? sender, EventArgs eventArgs)
    {
        if (!TryNormalizeServerUrl(_serverTextBox.Text, out var serverUri, out var error))
        {
            SetStatus(error, Error);
            return;
        }

        if (string.IsNullOrWhiteSpace(_tokenTextBox.Text))
        {
            SetStatus("Введите одноразовый код регистрации.", Error);
            return;
        }

        SetBusy(true);
        SetStatus("Проверяем сервер и регистрируем компьютер…", Slate);
        try
        {
            Enrollment = await AgentApiClient.EnrollAsync(
                serverUri,
                _tokenTextBox.Text,
                _cancellation.Token);
            ServerBaseUrl = serverUri.GetLeftPart(UriPartial.Path).TrimEnd('/');
            SetStatus("Компьютер подключён. MontageMonitor начинает работу.", Success);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (AgentEnrollmentException exception)
        {
            SetStatus(exception.Message, Error);
        }
        catch (HttpRequestException)
        {
            SetStatus("Сервер недоступен. Проверьте адрес и подключение к интернету.", Error);
        }
        catch (TaskCanceledException) when (!_cancellation.IsCancellationRequested)
        {
            SetStatus("Сервер не ответил за 20 секунд. Повторите попытку.", Error);
        }
        finally
        {
            if (!IsDisposed)
            {
                SetBusy(false);
            }
        }
    }

    private static bool TryNormalizeServerUrl(string value, out Uri serverUri, out string error)
    {
        error = string.Empty;
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var parsed) ||
            parsed.Scheme is not ("https" or "http"))
        {
            serverUri = null!;
            error = "Укажите полный адрес, например https://monitor.company.ru";
            return false;
        }

        if (parsed.Scheme != Uri.UriSchemeHttps && !parsed.IsLoopback)
        {
            serverUri = null!;
            error = "Для удалённого сервера обязательно используется HTTPS.";
            return false;
        }

        serverUri = new Uri(parsed.GetLeftPart(UriPartial.Path).TrimEnd('/') + "/");
        return true;
    }

    private void SetBusy(bool busy)
    {
        _connectButton.Enabled = !busy;
        _closeButton.Enabled = !busy;
        _serverTextBox.Enabled = !busy;
        _tokenTextBox.Enabled = !busy;
        UseWaitCursor = busy;
    }

    private void SetStatus(string message, Color color)
    {
        _statusLabel.Text = message;
        _statusLabel.ForeColor = color;
    }

    private static Panel CreateHeader()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Graphite,
            Padding = new Padding(34, 22, 34, 18),
        };
        var title = new Label
        {
            Text = "MONTAGE / MONITOR",
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 18f, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(30, 18),
        };
        var subtitle = new Label
        {
            Text = "Подключение рабочего компьютера",
            ForeColor = Color.FromArgb(188, 195, 204),
            Font = new Font("Segoe UI", 9.5f),
            AutoSize = true,
            Location = new Point(33, 52),
        };
        panel.Controls.Add(title);
        panel.Controls.Add(subtitle);

        var labels = new[] { "СЕРВЕР", "КОД", "ГОТОВО" };
        for (var index = 0; index < labels.Length; index++)
        {
            var x = 34 + index * 118;
            panel.Controls.Add(new Panel
            {
                BackColor = index == 0 ? Amber : Color.FromArgb(99, 108, 120),
                Size = new Size(7, 7),
                Location = new Point(x, 91),
            });
            panel.Controls.Add(new Label
            {
                Text = labels[index],
                ForeColor = index == 0 ? Amber : Color.FromArgb(164, 171, 181),
                Font = new Font("Consolas", 8f, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(x + 14, 86),
            });
            if (index < labels.Length - 1)
            {
                panel.Controls.Add(new Panel
                {
                    BackColor = Color.FromArgb(69, 76, 86),
                    Size = new Size(34, 1),
                    Location = new Point(x + 76, 94),
                });
            }
        }

        return panel;
    }

    private static Label CreateFieldLabel(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        ForeColor = Graphite,
        Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
        TextAlign = ContentAlignment.MiddleLeft,
    };

    private static TextBox CreateTextBox() => new()
    {
        Dock = DockStyle.Fill,
        Font = new Font("Segoe UI", 11f),
        BorderStyle = BorderStyle.FixedSingle,
        Margin = new Padding(0),
    };
}
