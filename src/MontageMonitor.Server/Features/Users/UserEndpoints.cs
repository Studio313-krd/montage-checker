using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MontageMonitor.Server.Domain;
using MontageMonitor.Server.Infrastructure.Persistence;
using MontageMonitor.Server.Infrastructure.Security;

namespace MontageMonitor.Server.Features.Users;

public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/users")
            .RequireAuthorization(SecurityPolicies.ManageUsers)
            .WithTags("Администрирование пользователей");

        group.MapGet("/", GetAllAsync)
            .WithName("GetUsers")
            .WithSummary("Получить пользователей");
        group.MapGet("/{id:guid}", GetByIdAsync)
            .WithName("GetUser")
            .WithSummary("Получить пользователя");
        group.MapPost("/", CreateAsync)
            .WithName("CreateUser")
            .WithSummary("Создать пользователя");
        group.MapPut("/{id:guid}", UpdateAsync)
            .WithName("UpdateUser")
            .WithSummary("Изменить пользователя и область доступа");
        group.MapPut("/{id:guid}/password", ChangePasswordAsync)
            .WithName("ChangeUserPassword")
            .WithSummary("Установить новый пароль пользователя");
        group.MapDelete("/{id:guid}", DeactivateAsync)
            .WithName("DeactivateUser")
            .WithSummary("Деактивировать пользователя");

        return endpoints;
    }

    private static async Task<IResult> GetAllAsync(
        MonitoringDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var users = await dbContext.Users.AsNoTracking()
            .OrderBy(item => item.DisplayName)
            .Select(item => new UserResponse(
                item.Id,
                item.Login,
                item.DisplayName,
                item.Role,
                item.IsActive,
                item.LastLoginAtUtc,
                dbContext.UserEmployeeAccess
                    .Where(access => access.UserId == item.Id)
                    .Select(access => access.EmployeeId)
                    .ToArray(),
                item.CreatedAtUtc,
                item.UpdatedAtUtc))
            .ToListAsync(cancellationToken);

        return Results.Ok(users);
    }

    private static async Task<IResult> GetByIdAsync(
        Guid id,
        MonitoringDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var user = await LoadResponseAsync(id, dbContext, cancellationToken);
        return user is null ? Results.NotFound() : Results.Ok(user);
    }

    private static async Task<IResult> CreateAsync(
        CreateUserRequest request,
        HttpContext httpContext,
        MonitoringDbContext dbContext,
        IPasswordHasher<User> passwordHasher,
        AuditWriter auditWriter,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var validation = ValidateUser(request.Login, request.DisplayName, request.Password, true);
        if (validation is not null)
        {
            return Results.ValidationProblem(validation);
        }

        if (!UserPermissionRules.CanAssignRole(httpContext.User, request.Role))
        {
            return Results.Forbid();
        }

        var employeeIds = NormalizeEmployeeIds(request.EmployeeIds);
        var accessValidation = await ValidateEmployeeAccessAsync(
            request.Role,
            employeeIds,
            dbContext,
            cancellationToken);
        if (accessValidation is not null)
        {
            return accessValidation;
        }

        var login = request.Login!.Trim();
        var normalizedLogin = OwnerBootstrapper.NormalizeLogin(login);
        if (await dbContext.Users.AnyAsync(
                item => item.NormalizedLogin == normalizedLogin,
                cancellationToken))
        {
            return Results.Conflict(new { message = "Пользователь с таким логином уже существует." });
        }

        var now = timeProvider.GetUtcNow();
        var user = new User
        {
            Login = login,
            NormalizedLogin = normalizedLogin,
            DisplayName = request.DisplayName!.Trim(),
            PasswordHash = string.Empty,
            Role = request.Role,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        user.PasswordHash = passwordHasher.HashPassword(user, request.Password!);
        dbContext.Users.Add(user);
        AddEmployeeAccess(user.Id, request.Role, employeeIds, dbContext, now);

        var actorId = httpContext.User.GetRequiredUserId();
        auditWriter.Add(
            httpContext,
            "user.created",
            "User",
            user.Id.ToString(),
            actorId,
            new { user.Login, role = user.Role.ToString(), employeeIds });
        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Created(
            $"/api/admin/users/{user.Id}",
            await LoadResponseAsync(user.Id, dbContext, cancellationToken));
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        UpdateUserRequest request,
        HttpContext httpContext,
        MonitoringDbContext dbContext,
        AuditWriter auditWriter,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var validation = ValidateUser(request.Login, request.DisplayName, null, false);
        if (validation is not null)
        {
            return Results.ValidationProblem(validation);
        }

        var target = await dbContext.Users.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (target is null)
        {
            return Results.NotFound();
        }

        var actorId = httpContext.User.GetRequiredUserId();
        if (!UserPermissionRules.CanModify(httpContext.User, target, request.Role))
        {
            return Results.Forbid();
        }

        if (actorId == target.Id && (!request.IsActive || request.Role != target.Role))
        {
            return Results.Conflict(new { message = "Нельзя деактивировать себя или изменить собственную роль." });
        }

        if (target.Role == UserRole.Owner && (!request.IsActive || request.Role != UserRole.Owner) &&
            !await HasAnotherActiveOwnerAsync(target.Id, dbContext, cancellationToken))
        {
            return Results.Conflict(new { message = "В системе должен остаться хотя бы один активный OWNER." });
        }

        var employeeIds = NormalizeEmployeeIds(request.EmployeeIds);
        var accessValidation = await ValidateEmployeeAccessAsync(
            request.Role,
            employeeIds,
            dbContext,
            cancellationToken);
        if (accessValidation is not null)
        {
            return accessValidation;
        }

        var login = request.Login!.Trim();
        var normalizedLogin = OwnerBootstrapper.NormalizeLogin(login);
        if (await dbContext.Users.AnyAsync(
                item => item.Id != id && item.NormalizedLogin == normalizedLogin,
                cancellationToken))
        {
            return Results.Conflict(new { message = "Пользователь с таким логином уже существует." });
        }

        var previousRole = target.Role;
        var wasActive = target.IsActive;
        var now = timeProvider.GetUtcNow();
        target.Login = login;
        target.NormalizedLogin = normalizedLogin;
        target.DisplayName = request.DisplayName!.Trim();
        target.Role = request.Role;
        target.IsActive = request.IsActive;
        target.UpdatedAtUtc = now;

        var previousAccess = await dbContext.UserEmployeeAccess
            .Where(access => access.UserId == target.Id)
            .ToListAsync(cancellationToken);
        dbContext.UserEmployeeAccess.RemoveRange(previousAccess);
        AddEmployeeAccess(target.Id, request.Role, employeeIds, dbContext, now);

        if (!request.IsActive || previousRole != request.Role)
        {
            await RevokeRefreshTokensAsync(target.Id, dbContext, now, cancellationToken);
        }

        auditWriter.Add(
            httpContext,
            "user.updated",
            "User",
            target.Id.ToString(),
            actorId,
            new
            {
                target.Login,
                previousRole = previousRole.ToString(),
                role = target.Role.ToString(),
                wasActive,
                target.IsActive,
                employeeIds,
            });
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Ok(await LoadResponseAsync(target.Id, dbContext, cancellationToken));
    }

    private static async Task<IResult> ChangePasswordAsync(
        Guid id,
        ChangeUserPasswordRequest request,
        HttpContext httpContext,
        MonitoringDbContext dbContext,
        IPasswordHasher<User> passwordHasher,
        AuditWriter auditWriter,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var passwordErrors = PasswordPolicy.Validate(request.NewPassword);
        if (passwordErrors.Length > 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.NewPassword)] = passwordErrors,
            });
        }

        var target = await dbContext.Users.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (target is null)
        {
            return Results.NotFound();
        }

        if (!UserPermissionRules.CanModify(httpContext.User, target, target.Role))
        {
            return Results.Forbid();
        }

        var now = timeProvider.GetUtcNow();
        target.PasswordHash = passwordHasher.HashPassword(target, request.NewPassword!);
        target.FailedLoginCount = 0;
        target.LockoutEndUtc = null;
        target.UpdatedAtUtc = now;
        await RevokeRefreshTokensAsync(target.Id, dbContext, now, cancellationToken);

        auditWriter.Add(
            httpContext,
            "user.password.changed",
            "User",
            target.Id.ToString(),
            httpContext.User.GetRequiredUserId());
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> DeactivateAsync(
        Guid id,
        HttpContext httpContext,
        MonitoringDbContext dbContext,
        AuditWriter auditWriter,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var target = await dbContext.Users.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (target is null)
        {
            return Results.NotFound();
        }

        var actorId = httpContext.User.GetRequiredUserId();
        if (!UserPermissionRules.CanModify(httpContext.User, target, target.Role))
        {
            return Results.Forbid();
        }

        if (actorId == target.Id)
        {
            return Results.Conflict(new { message = "Нельзя деактивировать собственную учётную запись." });
        }

        if (target.Role == UserRole.Owner &&
            !await HasAnotherActiveOwnerAsync(target.Id, dbContext, cancellationToken))
        {
            return Results.Conflict(new { message = "В системе должен остаться хотя бы один активный OWNER." });
        }

        var now = timeProvider.GetUtcNow();
        target.IsActive = false;
        target.UpdatedAtUtc = now;
        await RevokeRefreshTokensAsync(target.Id, dbContext, now, cancellationToken);
        auditWriter.Add(
            httpContext,
            "user.deactivated",
            "User",
            target.Id.ToString(),
            actorId);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static Dictionary<string, string[]>? ValidateUser(
        string? login,
        string? displayName,
        string? password,
        bool validatePassword)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(login) || login.Trim().Length is < 3 or > 100)
        {
            errors[nameof(login)] = ["Логин должен содержать от 3 до 100 символов."];
        }

        if (string.IsNullOrWhiteSpace(displayName) || displayName.Trim().Length > 200)
        {
            errors[nameof(displayName)] = ["Отображаемое имя обязательно и не должно превышать 200 символов."];
        }

        if (validatePassword)
        {
            var passwordErrors = PasswordPolicy.Validate(password);
            if (passwordErrors.Length > 0)
            {
                errors[nameof(password)] = passwordErrors;
            }
        }

        return errors.Count == 0 ? null : errors;
    }

    private static async Task<IResult?> ValidateEmployeeAccessAsync(
        UserRole role,
        IReadOnlyCollection<Guid> employeeIds,
        MonitoringDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (role is (UserRole.Owner or UserRole.Admin) && employeeIds.Count > 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["employeeIds"] = ["Для OWNER и ADMIN список не задаётся: им доступны все сотрудники."],
            });
        }

        if (employeeIds.Count == 0)
        {
            return null;
        }

        var existingCount = await dbContext.Employees.CountAsync(
            employee => employeeIds.Contains(employee.Id),
            cancellationToken);
        return existingCount == employeeIds.Count
            ? null
            : Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["employeeIds"] = ["Один или несколько сотрудников не существуют."],
            });
    }

    private static Guid[] NormalizeEmployeeIds(IReadOnlyCollection<Guid>? employeeIds) =>
        employeeIds?.Where(id => id != Guid.Empty).Distinct().ToArray() ?? [];

    private static void AddEmployeeAccess(
        Guid userId,
        UserRole role,
        IEnumerable<Guid> employeeIds,
        MonitoringDbContext dbContext,
        DateTimeOffset now)
    {
        if (role is UserRole.Owner or UserRole.Admin)
        {
            return;
        }

        dbContext.UserEmployeeAccess.AddRange(employeeIds.Select(employeeId => new UserEmployeeAccess
        {
            UserId = userId,
            EmployeeId = employeeId,
            CreatedAtUtc = now,
        }));
    }

    private static Task<bool> HasAnotherActiveOwnerAsync(
        Guid excludedUserId,
        MonitoringDbContext dbContext,
        CancellationToken cancellationToken) =>
        dbContext.Users.AnyAsync(
            item => item.Id != excludedUserId && item.Role == UserRole.Owner && item.IsActive,
            cancellationToken);

    private static Task<int> RevokeRefreshTokensAsync(
        Guid userId,
        MonitoringDbContext dbContext,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        dbContext.RefreshTokens
            .Where(item => item.UserId == userId && item.RevokedAtUtc == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.RevokedAtUtc, now)
                .SetProperty(item => item.UpdatedAtUtc, now), cancellationToken);

    private static Task<UserResponse?> LoadResponseAsync(
        Guid userId,
        MonitoringDbContext dbContext,
        CancellationToken cancellationToken) =>
        dbContext.Users.AsNoTracking()
            .Where(item => item.Id == userId)
            .Select(item => new UserResponse(
                item.Id,
                item.Login,
                item.DisplayName,
                item.Role,
                item.IsActive,
                item.LastLoginAtUtc,
                dbContext.UserEmployeeAccess
                    .Where(access => access.UserId == item.Id)
                    .Select(access => access.EmployeeId)
                    .ToArray(),
                item.CreatedAtUtc,
                item.UpdatedAtUtc))
            .SingleOrDefaultAsync(cancellationToken);
}
