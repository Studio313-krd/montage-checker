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
    private static readonly Color Error = Color.FromArgb(185, 68, 68);

    private readonly IAgentApiClient _apiClient;
    private readonly Guid? _currentSessionId;
    private readonly ComboBox _employeeComboBox;
    private readonly TextBox _pinTextBox;
    private readonly Button _confirmButton;
    private readonly Button _notEditorButton;
    private readonly Button _retryButton;
    private readonly Label _statusLabel;
    private readonly CancellationTokenSource _cancellation = new();
    private bool _allowClose;

    public OperatorSelectionDialog(IAgentApiClient apiClient, Guid? currentSessionId)
    {
        _apiClient = apiClient;
        _currentSessionId = currentSessionId;
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
            Text = "Выбор обязателен каждый день после 06:00. Данные будут записаны на выбранного монтажёра.",
            ForeColor = Slate,
            Font = new Font("Segoe UI", 10.5f),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.TopCenter,
        }, 0, 2);
        layout.Controls.Add(FieldLabel("Монтажёр"), 0, 3);
        _employeeComboBox = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = new Font("Segoe UI", 14f),
            Enabled = false,
            IntegralHeight = false,
            DropDownHeight = 280,
        };
        layout.Controls.Add(_employeeComboBox, 0, 4);
        layout.Controls.Add(FieldLabel("PIN-код из 4 цифр"), 0, 6);
        _pinTextBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 22f, FontStyle.Bold),
            MaxLength = 4,
            TextAlign = HorizontalAlignment.Center,
            UseSystemPasswordChar = true,
            Enabled = false,
        };
        _pinTextBox.KeyPress += (_, eventArgs) =>
        {
            if (!char.IsControl(eventArgs.KeyChar) && !char.IsDigit(eventArgs.KeyChar))
            {
                eventArgs.Handled = true;
            }
        };
        layout.Controls.Add(_pinTextBox, 0, 7);

        _confirmButton = new Button
        {
            Text = "НАЧАТЬ СМЕНУ",
            Dock = DockStyle.Fill,
            BackColor = Graphite,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI Semibold", 13f, FontStyle.Bold),
            Enabled = false,
            Cursor = Cursors.Hand,
            Margin = new Padding(0, 12, 0, 0),
        };
        _confirmButton.FlatAppearance.BorderSize = 0;
        _confirmButton.Click += ConfirmAsync;
        _employeeComboBox.SelectedIndexChanged += (_, _) =>
            _confirmButton.Enabled = _employeeComboBox.SelectedItem is OperatorItem && _pinTextBox.Text.Length == 4;
        _pinTextBox.TextChanged += (_, _) =>
            _confirmButton.Enabled = _employeeComboBox.SelectedItem is OperatorItem && _pinTextBox.Text.Length == 4;
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

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(0, 12, 0, 0),
        };
        _statusLabel = new Label
        {
            Text = "Загружаем список сотрудников…",
            ForeColor = Slate,
            Width = 560,
            Height = 42,
            TextAlign = ContentAlignment.TopCenter,
        };
        _retryButton = new Button
        {
            Text = "Повторить подключение",
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Graphite,
            Visible = false,
            Cursor = Cursors.Hand,
            Anchor = AnchorStyles.None,
        };
        _retryButton.Click += LoadEmployeesAsync;
        footer.Controls.Add(_statusLabel);
        footer.Controls.Add(_retryButton);
        layout.Controls.Add(footer, 0, 10);

        Shown += LoadEmployeesAsync;
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

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _cancellation.Cancel();
            _cancellation.Dispose();
        }

        base.Dispose(disposing);
    }

    private async void LoadEmployeesAsync(object? sender, EventArgs eventArgs)
    {
        SetBusy(true);
        _retryButton.Visible = false;
        SetStatus("Загружаем список сотрудников…", Slate);
        try
        {
            var response = await _apiClient.GetOperatorOptionsAsync(
                _currentSessionId,
                _cancellation.Token);
            if (response is null)
            {
                SetLoadError("Сервер недоступен. Проверьте интернет и повторите подключение.");
                return;
            }

            _employeeComboBox.Items.Clear();
            foreach (var employee in response.Employees)
            {
                _employeeComboBox.Items.Add(new OperatorItem(employee.EmployeeId, employee.Name));
            }

            if (_employeeComboBox.Items.Count == 0)
            {
                SetLoadError("В системе нет активных сотрудников. Обратитесь к администратору.");
                return;
            }

            _employeeComboBox.SelectedIndex = 0;
            _employeeComboBox.Enabled = true;
            _pinTextBox.Enabled = true;
            _pinTextBox.Focus();
            SetStatus("Выберите своё имя и введите выданный администратором PIN.", Slate);
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            if (!IsDisposed)
            {
                SetBusy(false);
            }
        }
    }

    private async void ConfirmAsync(object? sender, EventArgs eventArgs)
    {
        if (_employeeComboBox.SelectedItem is not OperatorItem employee || _pinTextBox.Text.Length != 4)
        {
            SetStatus("Выберите сотрудника и введите четыре цифры PIN-кода.", Error);
            return;
        }

        SetBusy(true);
        SetStatus("Проверяем PIN-код…", Slate);
        try
        {
            Session = await _apiClient.StartOperatorSessionAsync(
                employee.Id,
                _pinTextBox.Text,
                _cancellation.Token);
            _allowClose = true;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (OperatorSelectionException exception)
        {
            _pinTextBox.Clear();
            SetStatus(exception.Message, Error);
            _pinTextBox.Focus();
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
        var hasEmployees = _employeeComboBox.Items.Count > 0;
        _employeeComboBox.Enabled = !busy && hasEmployees;
        _pinTextBox.Enabled = !busy && hasEmployees;
        _confirmButton.Enabled = !busy && hasEmployees && _pinTextBox.Text.Length == 4;
        _retryButton.Enabled = !busy;
        _notEditorButton.Enabled = true;
        UseWaitCursor = busy;
    }

    private void SetLoadError(string message)
    {
        _employeeComboBox.Enabled = false;
        _pinTextBox.Enabled = false;
        _confirmButton.Enabled = false;
        _retryButton.Visible = true;
        SetStatus(message, Error);
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

    private sealed record OperatorItem(Guid Id, string Name)
    {
        public override string ToString() => Name;
    }
}
