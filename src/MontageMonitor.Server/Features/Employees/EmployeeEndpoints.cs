using Microsoft.EntityFrameworkCore;
using MontageMonitor.Server.Domain;
using MontageMonitor.Server.Infrastructure.Persistence;
using MontageMonitor.Server.Infrastructure.Security;

namespace MontageMonitor.Server.Features.Employees;

public static class EmployeeEndpoints
{
    public static IEndpointRouteBuilder MapEmployeeEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/employees")
            .RequireAuthorization(SecurityPolicies.ViewEmployees)
            .WithTags("Сотрудники");

        group.MapGet("/", GetAllAsync).WithName("GetEmployees").WithSummary("Получить список сотрудников");
        group.MapGet("/{id:guid}", GetByIdAsync).WithName("GetEmployee").WithSummary("Получить сотрудника");
        group.MapPost("/", CreateAsync)
            .RequireAuthorization(SecurityPolicies.ManageEmployees)
            .WithName("CreateEmployee").WithSummary("Создать сотрудника");
        group.MapPut("/{id:guid}", UpdateAsync)
            .RequireAuthorization(SecurityPolicies.ManageEmployees)
            .WithName("UpdateEmployee").WithSummary("Изменить сотрудника");
        group.MapDelete("/{id:guid}", DeactivateAsync)
            .RequireAuthorization(SecurityPolicies.ManageEmployees)
            .WithName("DeactivateEmployee").WithSummary("Деактивировать сотрудника");

        return endpoints;
    }

    private static async Task<IResult> GetAllAsync(
        bool? includeInactive,
        HttpContext httpContext,
        MonitoringDbContext dbContext,
        EmployeeAccessService employeeAccessService,
        CancellationToken cancellationToken)
    {
        var query = employeeAccessService.ApplyVisibility(
            dbContext.Employees.AsNoTracking(),
            httpContext.User);
        if (includeInactive is not true)
        {
            query = query.Where(employee => employee.IsActive);
        }

        var employees = await query
            .OrderBy(employee => employee.Name)
            .Select(employee => ToResponse(employee))
            .ToListAsync(cancellationToken);

        return Results.Ok(employees);
    }

    private static async Task<IResult> GetByIdAsync(
        Guid id,
        HttpContext httpContext,
        MonitoringDbContext dbContext,
        EmployeeAccessService employeeAccessService,
        CancellationToken cancellationToken)
    {
        if (!await employeeAccessService.CanViewAsync(id, httpContext.User, cancellationToken))
        {
            return Results.NotFound();
        }

        var employee = await dbContext.Employees.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

        return employee is null ? Results.NotFound() : Results.Ok(ToResponse(employee));
    }

    private static async Task<IResult> CreateAsync(
        CreateEmployeeRequest request,
        MonitoringDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var validation = Validate(request.Name, request.Login, request.ScreenshotIntervalMinutes, request.IdleThresholdSeconds);
        if (validation is not null)
        {
            return Results.ValidationProblem(validation);
        }

        var normalizedLogin = NormalizeLogin(request.Login);
        if (await dbContext.Employees.AnyAsync(item => item.NormalizedLogin == normalizedLogin, cancellationToken))
        {
            return Results.Conflict(new { message = "Сотрудник с таким логином уже существует." });
        }

        var employee = new Employee
        {
            Name = request.Name.Trim(),
            Login = request.Login.Trim(),
            NormalizedLogin = normalizedLogin,
            Department = NormalizeOptional(request.Department),
            ScreenshotEnabled = request.ScreenshotEnabled,
            ScreenshotIntervalMinutes = request.ScreenshotIntervalMinutes,
            IdleThresholdSeconds = request.IdleThresholdSeconds,
        };

        dbContext.Employees.Add(employee);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Created($"/api/employees/{employee.Id}", ToResponse(employee));
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        UpdateEmployeeRequest request,
        MonitoringDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var validation = Validate(request.Name, request.Login, request.ScreenshotIntervalMinutes, request.IdleThresholdSeconds);
        if (validation is not null)
        {
            return Results.ValidationProblem(validation);
        }

        var employee = await dbContext.Employees.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (employee is null)
        {
            return Results.NotFound();
        }

        var normalizedLogin = NormalizeLogin(request.Login);
        if (await dbContext.Employees.AnyAsync(
                item => item.Id != id && item.NormalizedLogin == normalizedLogin,
                cancellationToken))
        {
            return Results.Conflict(new { message = "Сотрудник с таким логином уже существует." });
        }

        employee.Name = request.Name.Trim();
        employee.Login = request.Login.Trim();
        employee.NormalizedLogin = normalizedLogin;
        employee.Department = NormalizeOptional(request.Department);
        employee.IsActive = request.IsActive;
        employee.ScreenshotEnabled = request.ScreenshotEnabled;
        employee.ScreenshotIntervalMinutes = request.ScreenshotIntervalMinutes;
        employee.IdleThresholdSeconds = request.IdleThresholdSeconds;
        employee.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Ok(ToResponse(employee));
    }

    private static async Task<IResult> DeactivateAsync(
        Guid id,
        MonitoringDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var employee = await dbContext.Employees.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (employee is null)
        {
            return Results.NotFound();
        }

        employee.IsActive = false;
        employee.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static Dictionary<string, string[]>? Validate(
        string name,
        string login,
        int screenshotIntervalMinutes,
        int idleThresholdSeconds)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200)
        {
            errors[nameof(name)] = ["Имя обязательно и не должно превышать 200 символов."];
        }

        if (string.IsNullOrWhiteSpace(login) || login.Trim().Length > 100)
        {
            errors[nameof(login)] = ["Логин обязателен и не должен превышать 100 символов."];
        }

        if (screenshotIntervalMinutes is < 1 or > 60)
        {
            errors[nameof(screenshotIntervalMinutes)] = ["Интервал скриншотов должен быть от 1 до 60 минут."];
        }

        if (idleThresholdSeconds < 0)
        {
            errors[nameof(idleThresholdSeconds)] = ["Порог простоя не может быть отрицательным."];
        }

        return errors.Count == 0 ? null : errors;
    }

    private static string NormalizeLogin(string login) => login.Trim().ToUpperInvariant();

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static EmployeeResponse ToResponse(Employee employee) => new(
        employee.Id,
        employee.Name,
        employee.Login,
        employee.Department,
        employee.IsActive,
        employee.ScreenshotEnabled,
        employee.ScreenshotIntervalMinutes,
        employee.IdleThresholdSeconds,
        employee.CreatedAtUtc,
        employee.UpdatedAtUtc);
}
