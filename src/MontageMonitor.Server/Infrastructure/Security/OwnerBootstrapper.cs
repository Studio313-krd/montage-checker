using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MontageMonitor.Server.Domain;
using MontageMonitor.Server.Infrastructure.Persistence;

namespace MontageMonitor.Server.Infrastructure.Security;

public static class OwnerBootstrapper
{
    public static async Task BootstrapOwnerAsync(
        this WebApplication app,
        CancellationToken cancellationToken = default)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MonitoringDbContext>();
        if (await dbContext.Users.AnyAsync(
                user => user.Role == UserRole.Owner && user.IsActive && user.PasswordHash != string.Empty,
                cancellationToken))
        {
            return;
        }

        var options = scope.ServiceProvider.GetRequiredService<IOptions<BootstrapOptions>>().Value;
        var login = options.OwnerLogin.Trim();
        var displayName = options.OwnerDisplayName.Trim();
        var errors = PasswordPolicy.Validate(options.OwnerPassword);

        if (login.Length is < 3 or > 100 || displayName.Length is < 1 or > 200 || errors.Length > 0)
        {
            var passwordError = errors.Length == 0 ? string.Empty : $" {string.Join(' ', errors)}";
            throw new InvalidOperationException(
                "В базе нет пользователей. Задайте корректные Bootstrap:OwnerLogin, " +
                $"Bootstrap:OwnerDisplayName и Bootstrap:OwnerPassword.{passwordError}");
        }

        if (await dbContext.Users.AnyAsync(
                user => user.NormalizedLogin == NormalizeLogin(login),
                cancellationToken))
        {
            throw new InvalidOperationException(
                "Нельзя создать первого OWNER: Bootstrap:OwnerLogin уже занят существующим пользователем.");
        }

        var now = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow();
        var user = new User
        {
            Login = login,
            NormalizedLogin = NormalizeLogin(login),
            DisplayName = displayName,
            PasswordHash = string.Empty,
            Role = UserRole.Owner,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
        user.PasswordHash = passwordHasher.HashPassword(user, options.OwnerPassword);
        dbContext.Users.Add(user);
        dbContext.AuditLogs.Add(new AuditLog
        {
            UserId = user.Id,
            Action = "user.bootstrap.created",
            EntityName = "User",
            EntityId = user.Id.ToString(),
            TimestampUtc = now,
        });
        await dbContext.SaveChangesAsync(cancellationToken);

        scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("OwnerBootstrap")
            .LogInformation("Создан первый пользователь OWNER с логином {OwnerLogin}.", login);
    }

    public static string NormalizeLogin(string login) => login.Trim().ToUpperInvariant();
}
