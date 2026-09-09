using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using MontageMonitor.Server.Domain;
using MontageMonitor.Server.Infrastructure.Persistence;

namespace MontageMonitor.Server.Infrastructure.Security;

public sealed class EmployeeAgentPasswordService(IDataProtectionProvider dataProtectionProvider)
{
    // Keep the original purpose string so production passwords remain decryptable after the rename.
    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector(
        "MontageMonitor.EmployeeOperatorPin.v1");

    public string Generate() => RandomNumberGenerator.GetInt32(10_000)
        .ToString("D4", CultureInfo.InvariantCulture);

    public string Protect(string password) => _protector.Protect(password);

    public string? Reveal(string? protectedPassword)
    {
        if (string.IsNullOrWhiteSpace(protectedPassword))
        {
            return null;
        }

        try
        {
            return _protector.Unprotect(protectedPassword);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    public bool Verify(string? protectedPassword, string suppliedPassword)
    {
        var expected = Reveal(protectedPassword);
        if (expected is null)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(suppliedPassword));
    }
}

public static class EmployeeAgentPasswordProvisioningExtensions
{
    public static async Task ProvisionEmployeeAgentPasswordsAsync(
        this WebApplication app,
        CancellationToken cancellationToken = default)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MonitoringDbContext>();
        var passwordService = scope.ServiceProvider.GetRequiredService<EmployeeAgentPasswordService>();
        var employees = await dbContext.Employees
            .Where(item => item.OperatorPinProtected == null)
            .ToListAsync(cancellationToken);
        if (employees.Count == 0)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var employee in employees)
        {
            employee.OperatorPinProtected = passwordService.Protect(passwordService.Generate());
            employee.UpdatedAtUtc = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
