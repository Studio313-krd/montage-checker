using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using MontageMonitor.Server.Domain;
using MontageMonitor.Server.Infrastructure.Persistence;
using MontageMonitor.Server.Infrastructure.Security;

namespace MontageMonitor.Server.Features.Employees;

public sealed class EmployeeAccessService(MonitoringDbContext dbContext)
{
    public IQueryable<Employee> ApplyVisibility(
        IQueryable<Employee> employees,
        ClaimsPrincipal principal)
    {
        if (UserPermissionRules.HasGlobalEmployeeVisibility(principal))
        {
            return employees;
        }

        var userId = principal.GetRequiredUserId();
        return employees.Where(employee => dbContext.UserEmployeeAccess.Any(
            access => access.UserId == userId && access.EmployeeId == employee.Id));
    }

    public Task<bool> CanViewAsync(
        Guid employeeId,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        if (UserPermissionRules.HasGlobalEmployeeVisibility(principal))
        {
            return Task.FromResult(true);
        }

        var userId = principal.GetRequiredUserId();
        return dbContext.UserEmployeeAccess.AnyAsync(
            access => access.UserId == userId && access.EmployeeId == employeeId,
            cancellationToken);
    }
}
