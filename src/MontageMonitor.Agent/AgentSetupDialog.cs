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
    private static readonly Color DisabledButton = Color.FromArgb(213, 217, 223);
    private static readonly Color Success = Color.FromArgb(47, 125, 97);
    private static readonly Color Error = Color.FromArgb(185, 68, 68);

    private readonly ComboBox _employeeComboBox;
    private readonly Button _refreshEmployeesButton;
    private readonly TextBox _passwordTextBox;
    private readonly Button _loginButton;
    private readonly Button _closeButton;
    private readonly Label _statusLabel;
    private readonly CancellationTokenSource _cancellation = new();
    private bool _busy;

    public AgentSetupDialog()
    {
        Text = "Вход в MontageMonitor";
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
        rootLayout.Controls.Add(CreateHeader(), 0, 0);

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

        layout.Controls.Add(CreateFieldLabel("Сотрудник"), 0, 0);
        var employeeRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0),
        };
        employeeRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        employeeRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
        _employeeComboBox = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            DisplayMember = nameof(AgentOperatorOption.Name),
            Font = new Font("Segoe UI", 11f),
            Enabled = false,
            Margin = new Padding(0),
        };
        _employeeComboBox.SelectedIndexChanged += (_, _) => UpdateLoginButton();
        _refreshEmployeesButton = new Button
        {
            Text = "Обновить",
            Dock = DockStyle.Fill,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Graphite,
            Cursor = Cursors.Hand,
            Margin = new Padding(10, 0, 0, 0),
        };
        _refreshEmployeesButton.FlatAppearance.BorderColor = Color.FromArgb(195, 199, 205);
        _refreshEmployeesButton.Click += async (_, _) => await LoadEmployeesAsync();
        employeeRow.Controls.Add(_employeeComboBox, 0, 0);
        employeeRow.Controls.Add(_refreshEmployeesButton, 1, 0);
        layout.Controls.Add(employeeRow, 0, 1);

        layout.Controls.Add(CreateFieldLabel("Пароль из 4 цифр"), 0, 3);
        _passwordTextBox = CreateTextBox();
        _passwordTextBox.MaxLength = 4;
        _passwordTextBox.UseSystemPasswordChar = true;
        _passwordTextBox.TextAlign = HorizontalAlignment.Center;
        _passwordTextBox.KeyPress += AllowDigitsOnly;
        _passwordTextBox.TextChanged += (_, _) => UpdateLoginButton();
        layout.Controls.Add(_passwordTextBox, 0, 4);

        _statusLabel = new Label
        {
            Text = "Загружаем список сотрудников…",
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
        _loginButton = new Button
        {
            Text = "ВОЙТИ И ПОДКЛЮЧИТЬ",
            AutoSize = false,
            Size = new Size(205, 38),
            BackColor = DisabledButton,
            ForeColor = Slate,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
            Enabled = false,
            UseVisualStyleBackColor = false,
        };
        _loginButton.FlatAppearance.BorderSize = 0;
        _loginButton.Click += LoginAsync;
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
        buttons.Controls.Add(_loginButton);
        buttons.Controls.Add(_closeButton);
        layout.Controls.Add(buttons, 0, 7);

        Shown += async (_, _) => await LoadEmployeesAsync();
        AcceptButton = _loginButton;
        CancelButton = _closeButton;
    }

    public AgentLoginResponse? LoginResult { get; private set; }

    protected override void OnFormClosed(FormClosedEventArgs eventArgs)
    {
        _cancellation.Cancel();
        _cancellation.Dispose();
        base.OnFormClosed(eventArgs);
    }

    private async void LoginAsync(object? sender, EventArgs eventArgs)
    {
        if (!AgentLoginInput.IsValid(SelectedEmployeeId, _passwordTextBox.Text))
        {
            SetStatus("Выберите сотрудника и введите пароль из четырёх цифр.", Error);
            return;
        }

        SetBusy(true);
        SetStatus("Проверяем данные и подключаем компьютер…", Slate);
        try
        {
            LoginResult = await AgentApiClient.LoginAsync(
                SelectedEmployeeId!.Value,
                _passwordTextBox.Text,
                _cancellation.Token);
            SetStatus("Компьютер подключён. Мониторинг запущен.", Success);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (AgentLoginException exception)
        {
            _passwordTextBox.Clear();
            SetStatus(exception.Message, Error);
            _passwordTextBox.Focus();
        }
        catch (HttpRequestException)
        {
            SetStatus("Сервер недоступен. Проверьте подключение к интернету.", Error);
        }
        catch (TaskCanceledException) when (!_cancellation.IsCancellationRequested)
        {
            SetStatus("Сервер не ответил за 20 секунд. Повторите попытку.", Error);
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

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _closeButton.Enabled = !busy;
        _employeeComboBox.Enabled = !busy && _employeeComboBox.Items.Count > 0;
        _refreshEmployeesButton.Enabled = !busy;
        _passwordTextBox.Enabled = !busy;
        UseWaitCursor = busy;
        UpdateLoginButton();
    }

    private void UpdateLoginButton()
    {
        var enabled = !_busy && AgentLoginInput.IsValid(SelectedEmployeeId, _passwordTextBox.Text);
        _loginButton.Enabled = enabled;
        _loginButton.BackColor = enabled ? Amber : DisabledButton;
        _loginButton.ForeColor = enabled ? Graphite : Slate;
        _loginButton.Cursor = enabled ? Cursors.Hand : Cursors.Default;
    }

    private void SetStatus(string message, Color color)
    {
        _statusLabel.Text = message;
        _statusLabel.ForeColor = color;
    }

    private Guid? SelectedEmployeeId =>
        (_employeeComboBox.SelectedItem as AgentOperatorOption)?.EmployeeId;

    private async Task LoadEmployeesAsync()
    {
        var previouslySelectedId = SelectedEmployeeId;
        SetBusy(true);
        SetStatus("Загружаем список сотрудников…", Slate);
        try
        {
            var response = await AgentApiClient.GetLoginOptionsAsync(_cancellation.Token);
            _employeeComboBox.BeginUpdate();
            try
            {
                _employeeComboBox.Items.Clear();
                foreach (var employee in response.Employees)
                {
                    _employeeComboBox.Items.Add(employee);
                }

                var selectedIndex = response.Employees
                    .Select((employee, index) => new { employee.EmployeeId, Index = index })
                    .FirstOrDefault(item => item.EmployeeId == previouslySelectedId)?.Index ?? -1;
                _employeeComboBox.SelectedIndex = selectedIndex >= 0
                    ? selectedIndex
                    : (_employeeComboBox.Items.Count > 0 ? 0 : -1);
            }
            finally
            {
                _employeeComboBox.EndUpdate();
            }

            SetStatus(
                _employeeComboBox.Items.Count > 0
                    ? "Выберите своё имя и введите выданный администратором пароль."
                    : "Активных сотрудников нет. Обратитесь к администратору.",
                _employeeComboBox.Items.Count > 0 ? Slate : Error);
            _passwordTextBox.Focus();
        }
        catch (AgentLoginException exception)
        {
            _employeeComboBox.Items.Clear();
            SetStatus(exception.Message + " Нажмите «Обновить».", Error);
        }
        catch (HttpRequestException)
        {
            _employeeComboBox.Items.Clear();
            SetStatus("Сервер недоступен. Проверьте интернет и нажмите «Обновить».", Error);
        }
        catch (TaskCanceledException) when (!_cancellation.IsCancellationRequested)
        {
            _employeeComboBox.Items.Clear();
            SetStatus("Сервер не ответил. Нажмите «Обновить».", Error);
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

    private static void AllowDigitsOnly(object? sender, KeyPressEventArgs eventArgs)
    {
        if (!char.IsControl(eventArgs.KeyChar) && !char.IsAsciiDigit(eventArgs.KeyChar))
        {
            eventArgs.Handled = true;
        }
    }

    private static Panel CreateHeader()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Graphite,
            Padding = new Padding(34, 22, 34, 18),
        };
        panel.Controls.Add(new Label
        {
            Text = "MONTAGE / MONITOR",
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 18f, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(30, 18),
        });
        panel.Controls.Add(new Label
        {
            Text = "Вход на рабочем компьютере",
            ForeColor = Color.FromArgb(188, 195, 204),
            Font = new Font("Segoe UI", 9.5f),
            AutoSize = true,
            Location = new Point(33, 52),
        });

        var labels = new[] { "СОТРУДНИК", "ПАРОЛЬ", "ГОТОВО" };
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

internal static class AgentLoginInput
{
    public static bool IsValid(Guid? employeeId, string? password) =>
        employeeId.HasValue &&
        employeeId != Guid.Empty &&
        password is { Length: 4 } &&
        password.All(char.IsAsciiDigit);
}
