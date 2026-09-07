using MontageMonitor.Server.Domain;

namespace MontageMonitor.Server.Features.Users;

public sealed record UserResponse(
    Guid Id,
    string Login,
    string DisplayName,
    UserRole Role,
    bool IsActive,
    DateTimeOffset? LastLoginAtUtc,
    IReadOnlyCollection<Guid> EmployeeIds,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record CreateUserRequest(
    string? Login,
    string? DisplayName,
    string? Password,
    UserRole Role,
    IReadOnlyCollection<Guid>? EmployeeIds);

public sealed record UpdateUserRequest(
    string? Login,
    string? DisplayName,
    UserRole Role,
    bool IsActive,
    IReadOnlyCollection<Guid>? EmployeeIds);

public sealed record ChangeUserPasswordRequest(string? NewPassword);
