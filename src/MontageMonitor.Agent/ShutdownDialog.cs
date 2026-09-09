using System.Security.Cryptography;
using System.Text;

namespace MontageMonitor.Agent;

internal sealed class ShutdownDialog : Form
{
    private const string ExpectedPasswordHash =
        "9AAED417FFB6F21C6DDB2D58D014162FEADD262774F6B82CAEE848B2248C6B37";

    private readonly TextBox _password = new()
    {
        Dock = DockStyle.Fill,
        UseSystemPasswordChar = true,
    };

    private readonly Label _error = new()
    {
        AutoSize = true,
        ForeColor = Color.Firebrick,
        Text = " ",
    };

    public ShutdownDialog()
    {
        Text = "Отключение MontageMonitor";
        Icon = AppBranding.Icon;
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(390, 168);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;

        var confirmButton = new Button { AutoSize = true, Text = "Отключить" };
        confirmButton.Click += ConfirmButtonOnClick;

        var cancelButton = new Button
        {
            AutoSize = true,
            DialogResult = DialogResult.Cancel,
            Text = "Отмена",
        };

        AcceptButton = confirmButton;
        CancelButton = cancelButton;

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
        };
        buttons.Controls.Add(cancelButton);
        buttons.Controls.Add(confirmButton);

        var layout = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            RowCount = 4,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label
        {
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 8),
            Text = "Введите пароль, чтобы отключить агент:",
        });
        layout.Controls.Add(_password);
        layout.Controls.Add(_error);
        layout.Controls.Add(buttons);
        Controls.Add(layout);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _password.Focus();
    }

    private void ConfirmButtonOnClick(object? sender, EventArgs e)
    {
        if (PasswordMatches(_password.Text))
        {
            DialogResult = DialogResult.OK;
            Close();
            return;
        }

        _password.Clear();
        _error.Text = "Неверный пароль.";
        _password.Focus();
    }

    internal static bool PasswordMatches(string password)
    {
        var input = Encoding.UTF8.GetBytes($"MontageMonitor:shutdown:v1:{password}");
        var actualHash = SHA256.HashData(input);
        var expectedHash = Convert.FromHexString(ExpectedPasswordHash);
        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }
}
