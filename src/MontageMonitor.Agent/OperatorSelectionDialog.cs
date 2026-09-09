using System.Drawing;
using MontageMonitor.Agent.Abstractions;
using MontageMonitor.Agent.Networking;
using MontageMonitor.Shared.Contracts.Agent;

namespace MontageMonitor.Agent;

internal sealed class OperatorSelectionDialog : Form
{
    private static readonly Color Graphite = Color.FromArgb(21, 25, 31);
    private static readonly Color Canvas = Color.FromArgb(242, 243, 245);
    private static readonly Color Amber = Color.FromArgb(244, 185, 66);
    private static readonly Color Slate = Color.FromArgb(80, 89, 103);
    private static readonly Color DisabledButton = Color.FromArgb(213, 217, 223);
    private static readonly Color Error = Color.FromArgb(185, 68, 68);

    private readonly IAgentApiClient _apiClient;
    private readonly TextBox _loginTextBox;
    private readonly TextBox _passwordTextBox;
    private readonly Button _confirmButton;
    private readonly Button _notEditorButton;
    private readonly Label _statusLabel;
    private readonly CancellationTokenSource _cancellation = new();
    private bool _allowClose;
    private bool _busy;

    public OperatorSelectionDialog(IAgentApiClient apiClient)
    {
        _apiClient = apiClient;
        Text = "Кто сегодня работает? — MontageMonitor";
        Icon = AppBranding.Icon;
        WindowState = FormWindowState.Maximized;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;
        ShowInTaskbar = true;
        BackColor = Graphite;
        ForeColor = Graphite;
        Font = new Font("Segoe UI", 11f);
        KeyPreview = true;
        AutoScaleMode = AutoScaleMode.Dpi;

        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Graphite,
            ColumnCount = 3,
            RowCount = 3,
            Padding = new Padding(36),
        };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 680));
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 650));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        Controls.Add(shell);

        var card = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Canvas,
            Padding = new Padding(58, 46, 58, 42),
            Margin = new Padding(0),
        };
        shell.Controls.Add(card, 1, 1);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 11,
            Margin = new Padding(0),
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 29));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 29));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        card.Controls.Add(layout);

        layout.Controls.Add(new Label
        {
            Text = "MONTAGE / MONITOR",
            ForeColor = Amber,
            Font = new Font("Segoe UI Semibold", 12f, FontStyle.Bold),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
        }, 0, 0);
        layout.Controls.Add(new Label
        {
            Text = "Кто сегодня работает?",
            ForeColor = Graphite,
            Font = new Font("Segoe UI Semibold", 28f, FontStyle.Bold),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
        }, 0, 1);
        layout.Controls.Add(new Label
        {
            Text = "Вход обязателен каждый день после 06:00. Данные будут записаны на вошедшего монтажёра.",
            ForeColor = Slate,
            Font = new Font("Segoe UI", 10.5f),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.TopCenter,
        }, 0, 2);

        layout.Controls.Add(FieldLabel("Логин"), 0, 3);
        _loginTextBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 14f),
            MaxLength = 100,
        };
        _loginTextBox.TextChanged += (_, _) => UpdateConfirmButton();
        layout.Controls.Add(_loginTextBox, 0, 4);

        layout.Controls.Add(FieldLabel("Пароль из 4 цифр"), 0, 6);
        _passwordTextBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 22f, FontStyle.Bold),
            MaxLength = 4,
            TextAlign = HorizontalAlignment.Center,
            UseSystemPasswordChar = true,
        };
        _passwordTextBox.KeyPress += (_, eventArgs) =>
        {
            if (!char.IsControl(eventArgs.KeyChar) && !char.IsAsciiDigit(eventArgs.KeyChar))
            {
                eventArgs.Handled = true;
            }
        };
        _passwordTextBox.TextChanged += (_, _) => UpdateConfirmButton();
        layout.Controls.Add(_passwordTextBox, 0, 7);

        _confirmButton = new Button
        {
            Text = "ВОЙТИ И НАЧАТЬ СМЕНУ",
            Dock = DockStyle.Fill,
            BackColor = DisabledButton,
            ForeColor = Slate,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI Semibold", 13f, FontStyle.Bold),
            Enabled = false,
            UseVisualStyleBackColor = false,
            Margin = new Padding(0, 12, 0, 0),
        };
        _confirmButton.FlatAppearance.BorderSize = 0;
        _confirmButton.Click += ConfirmAsync;
        layout.Controls.Add(_confirmButton, 0, 8);

        _notEditorButton = new Button
        {
            Text = "Я НЕ МОНТАЖЕР",
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(217, 78, 78),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI Semibold", 13f, FontStyle.Bold),
            Cursor = Cursors.Hand,
            Margin = new Padding(0, 12, 0, 0),
        };
        _notEditorButton.FlatAppearance.BorderSize = 0;
        _notEditorButton.Click += (_, _) => CloseAsNonEditor();
        layout.Controls.Add(_notEditorButton, 0, 9);

        _statusLabel = new Label
        {
            Text = "Введите свой логин и выданный администратором пароль.",
            ForeColor = Slate,
            Dock = DockStyle.Fill,
            Padding = new Padding(0, 14, 0, 0),
            TextAlign = ContentAlignment.TopCenter,
        };
        layout.Controls.Add(_statusLabel, 0, 10);

        Shown += (_, _) => _loginTextBox.Focus();
        FormClosing += PreventClosing;
        KeyDown += (_, eventArgs) =>
        {
            if (eventArgs.Alt && eventArgs.KeyCode == Keys.F4)
            {
                eventArgs.Handled = true;
                eventArgs.SuppressKeyPress = true;
            }
        };
        AcceptButton = _confirmButton;
    }

    public AgentOperatorSessionResponse? Session { get; private set; }

    public bool ExitRequested { get; private set; }

    public bool AuthenticationRequired { get; private set; }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _cancellation.Cancel();
            _cancellation.Dispose();
        }

        base.Dispose(disposing);
    }

    private async void ConfirmAsync(object? sender, EventArgs eventArgs)
    {
        if (!AgentLoginInput.IsValid(_loginTextBox.Text, _passwordTextBox.Text))
        {
            SetStatus("Введите логин и пароль из четырёх цифр.", Error);
            return;
        }

        SetBusy(true);
        SetStatus("Проверяем логин и пароль…", Slate);
        try
        {
            Session = await _apiClient.StartOperatorSessionAsync(
                _loginTextBox.Text,
                _passwordTextBox.Text,
                _cancellation.Token);
            _allowClose = true;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (OperatorSelectionException exception)
        {
            _passwordTextBox.Clear();
            SetStatus(exception.Message, Error);
            _passwordTextBox.Focus();
        }
        catch (AgentAuthenticationRequiredException)
        {
            AuthenticationRequired = true;
            _allowClose = true;
            DialogResult = DialogResult.Retry;
            Close();
        }
        catch (HttpRequestException)
        {
            SetStatus("Сервер недоступен. Проверьте интернет и повторите попытку.", Error);
        }
        catch (TaskCanceledException) when (!_cancellation.IsCancellationRequested)
        {
            SetStatus("Сервер не ответил. Повторите попытку.", Error);
        }
        finally
        {
            if (!IsDisposed)
            {
                SetBusy(false);
            }
        }
    }

    private void CloseAsNonEditor()
    {
        ExitRequested = true;
        _allowClose = true;
        DialogResult = DialogResult.Abort;
        Close();
    }

    private void PreventClosing(object? sender, FormClosingEventArgs eventArgs)
    {
        if (!_allowClose)
        {
            eventArgs.Cancel = true;
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _loginTextBox.Enabled = !busy;
        _passwordTextBox.Enabled = !busy;
        _notEditorButton.Enabled = !busy;
        UseWaitCursor = busy;
        UpdateConfirmButton();
    }

    private void UpdateConfirmButton()
    {
        var enabled = !_busy && AgentLoginInput.IsValid(_loginTextBox.Text, _passwordTextBox.Text);
        _confirmButton.Enabled = enabled;
        _confirmButton.BackColor = enabled ? Amber : DisabledButton;
        _confirmButton.ForeColor = enabled ? Graphite : Slate;
        _confirmButton.Cursor = enabled ? Cursors.Hand : Cursors.Default;
    }

    private void SetStatus(string message, Color color)
    {
        _statusLabel.Text = message;
        _statusLabel.ForeColor = color;
    }

    private static Label FieldLabel(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        ForeColor = Graphite,
        Font = new Font("Segoe UI Semibold", 10f, FontStyle.Bold),
        TextAlign = ContentAlignment.BottomLeft,
    };
}
