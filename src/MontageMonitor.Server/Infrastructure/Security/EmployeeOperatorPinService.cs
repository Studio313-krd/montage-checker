using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using MontageMonitor.Server.Domain;
using MontageMonitor.Server.Infrastructure.Persistence;

namespace MontageMonitor.Server.Infrastructure.Security;

public sealed class EmployeeOperatorPinService(IDataProtectionProvider dataProtectionProvider)
{
    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector(
        "MontageMonitor.EmployeeOperatorPin.v1");

    public string Generate() => RandomNumberGenerator.GetInt32(10_000)
        .ToString("D4", CultureInfo.InvariantCulture);

    public string Protect(string pin) => _protector.Protect(pin);

    public string? Reveal(string? protectedPin)
    {
        if (string.IsNullOrWhiteSpace(protectedPin))
        {
            return null;
        }

        try
        {
            return _protector.Unprotect(protectedPin);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    public bool Verify(string? protectedPin, string suppliedPin)
    {
        var expected = Reveal(protectedPin);
        if (expected is null)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(suppliedPin));
    }
}

public static class EmployeeOperatorPinProvisioningExtensions
{
    public static async Task ProvisionEmployeeOperatorPinsAsync(
        this WebApplication app,
        CancellationToken cancellationToken = default)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MonitoringDbContext>();
        var pinService = scope.ServiceProvider.GetRequiredService<EmployeeOperatorPinService>();
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
            employee.OperatorPinProtected = pinService.Protect(pinService.Generate());
            employee.UpdatedAtUtc = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
