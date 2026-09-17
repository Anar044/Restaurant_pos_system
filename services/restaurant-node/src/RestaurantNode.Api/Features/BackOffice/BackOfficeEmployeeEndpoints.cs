using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RestaurantNode.Api.Domain;
using RestaurantNode.Api.Infrastructure;
using RestaurantNode.Api.Security;

namespace RestaurantNode.Api.Features.BackOffice;

public static class BackOfficeEmployeeEndpoints
{
    public static IEndpointRouteBuilder MapBackOfficeEmployeeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/backoffice/employees")
            .RequireAuthorization(Permissions.BackOfficeRead);

        group.MapGet("", async (
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryGetRestaurantId(user, out var restaurantId))
                return Results.Unauthorized();

            var roles = await db.Roles
                .AsNoTracking()
                .Where(x => x.RestaurantId == restaurantId)
                .OrderBy(x => x.Name)
                .Select(role => new
                {
                    id = role.Id,
                    name = role.Name,
                    permissions = role.Permissions,
                    employeeCount = db.Employees.Count(employee => employee.RestaurantId == restaurantId && employee.RoleId == role.Id),
                    activeEmployeeCount = db.Employees.Count(employee => employee.RestaurantId == restaurantId && employee.RoleId == role.Id && employee.IsActive)
                })
                .ToListAsync(ct);

            var employees = await db.Employees
                .AsNoTracking()
                .Where(x => x.RestaurantId == restaurantId)
                .OrderByDescending(x => x.IsActive)
                .ThenBy(x => x.Name)
                .Select(employee => new
                {
                    id = employee.Id,
                    name = employee.Name,
                    roleId = employee.RoleId,
                    roleName = db.Roles
                        .Where(role => role.Id == employee.RoleId)
                        .Select(role => role.Name)
                        .FirstOrDefault(),
                    isActive = employee.IsActive,
                    createdAt = employee.CreatedAt
                })
                .ToListAsync(ct);

            return Results.Ok(new
            {
                employees,
                roles,
                availablePermissions = Permissions.All
            });
        });

        group.MapPost("/roles", async (
            CreateRoleRequest request,
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryGetRestaurantId(user, out var restaurantId))
                return Results.Unauthorized();

            var name = NormalizeName(request.Name, 100);
            if (name is null)
                return Results.BadRequest(new { message = "Role name is required and must be 100 characters or fewer." });

            var permissions = NormalizePermissions(request.Permissions, out var invalidPermission);
            if (invalidPermission is not null)
                return Results.BadRequest(new { message = $"Unknown permission: {invalidPermission}." });

            var duplicate = await db.Roles.AnyAsync(
                x => x.RestaurantId == restaurantId && x.Name.ToLower() == name.ToLower(),
                ct);
            if (duplicate)
                return Results.Conflict(new { message = "A role with this name already exists." });

            var role = new Role
            {
                RestaurantId = restaurantId,
                Name = name,
                Permissions = permissions
            };

            db.Roles.Add(role);
            AddAudit(db, user, restaurantId, "ROLE_CREATED", "Role", role.Id, new
            {
                role.Name,
                role.Permissions
            });
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/v1/backoffice/employees/roles/{role.Id}", ToRoleResponse(role, 0, 0));
        }).RequireAuthorization(Permissions.EmployeesManage);

        group.MapPut("/roles/{roleId:guid}", async (
            Guid roleId,
            UpdateRoleRequest request,
            ClaimsPrincipal user,
            RestaurantDbContext db,
            CancellationToken ct) =>
        {
            if (!TryGetRestaurantId(user, out var restaurantId))
                return Results.Unauthorized();

            var role = await db.Roles.FirstOrDefaultAsync(
                x => x.Id == roleId && x.RestaurantId == restaurantId,
                ct);
            if (role is null)
                return Results.NotFound();

            var name = NormalizeName(request.Name, 100);
            if (name is null)
                return Results.BadRequest(new { message = "Role name is required and must be 100 characters or fewer." });

            var permissions = NormalizePermissions(request.Permissions, out var invalidPermission);
            if (invalidPermission is not null)
                return Results.BadRequest(new { message = $"Unknown permission: {invalidPermission}." });

            var duplicate = await db.Roles.AnyAsync(
                x => x.RestaurantId == restaurantId && x.Id != roleId && x.Name.ToLower() == name.ToLower(),
                ct);
            if (duplicate)
                return Results.Conflict(new { message = "A role with this name already exists." });

            if (TryGetEmployeeId(user, out var currentEmployeeId))
            {
                var currentEmployeeUsesRole = await db.Employees.AnyAsync(
                    x => x.Id == currentEmployeeId && x.RestaurantId == restaurantId && x.RoleId == roleId,
                    ct);

                if (currentEmployeeUsesRole &&
                    (!permissions.Contains(Permissions.BackOfficeRead) || !permissions.Contains(Permissions.EmployeesManage)))
                {
                    return Results.Conflict(new
                    {
                        message = "You cannot remove BackOffice or employee-management access from the role you are currently using. Assign yourself another administrative role first."
                    });
                }
            }

            role.Name = name;
            role.Permissions = permissions;

            AddAudit(db, user, restaurantId, "ROLE_UPDATED", "Role", role.Id, new
            {
                role.Name,
                role.Permissions
            });
            await db.SaveChangesAsync(ct);

            var employeeCount = await db.Employees.CountAsync(
                x => x.RestaurantId == restaurantId && x.RoleId == role.Id,
                ct);
            var activeEmployeeCount = await db.Employees.CountAsync(
                x => x.RestaurantId == restaurantId && x.RoleId == role.Id && x.IsActive,
                ct);

            return Results.Ok(ToRoleResponse(role, employeeCount, activeEmployeeCount));
        }).RequireAuthorization(Permissions.EmployeesManage);

        group.MapPost("", async (
            CreateEmployeeRequest request,
            ClaimsPrincipal user,
            RestaurantDbContext db,
            PinHasher pinHasher,
            CancellationToken ct) =>
        {
            if (!TryGetRestaurantId(user, out var restaurantId))
                return Results.Unauthorized();

            var name = NormalizeName(request.Name, 160);
            if (name is null)
                return Results.BadRequest(new { message = "Employee name is required and must be 160 characters or fewer." });

            var pinError = ValidatePin(request.Pin);
            if (pinError is not null)
                return Results.BadRequest(new { message = pinError });

            var role = await db.Roles
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == request.RoleId && x.RestaurantId == restaurantId, ct);
            if (role is null)
                return Results.BadRequest(new { message = "Role was not found." });

            if (await PinExists(db, pinHasher, restaurantId, request.Pin, excludeEmployeeId: null, ct))
                return Results.Conflict(new { message = "This PIN is already used by another employee in this restaurant." });

            var employee = new Employee
            {
                RestaurantId = restaurantId,
                RoleId = role.Id,
                Name = name,
                PinHash = pinHasher.Hash(restaurantId, request.Pin),
                IsActive = true
            };

            db.Employees.Add(employee);
            AddAudit(db, user, restaurantId, "EMPLOYEE_CREATED", "Employee", employee.Id, new
            {
                employee.Name,
                employee.RoleId,
                roleName = role.Name,
                employee.IsActive
            });
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/v1/backoffice/employees/{employee.Id}", ToEmployeeResponse(employee, role.Name));
        }).RequireAuthorization(Permissions.EmployeesManage);

        group.MapPut("/{employeeId:guid}", async (
            Guid employeeId,
            UpdateEmployeeRequest request,
            ClaimsPrincipal user,
            RestaurantDbContext db,
            PinHasher pinHasher,
            CancellationToken ct) =>
        {
            if (!TryGetRestaurantId(user, out var restaurantId))
                return Results.Unauthorized();

            var employee = await db.Employees.FirstOrDefaultAsync(
                x => x.Id == employeeId && x.RestaurantId == restaurantId,
                ct);
            if (employee is null)
                return Results.NotFound();

            var name = NormalizeName(request.Name, 160);
            if (name is null)
                return Results.BadRequest(new { message = "Employee name is required and must be 160 characters or fewer." });

            var role = await db.Roles
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == request.RoleId && x.RestaurantId == restaurantId, ct);
            if (role is null)
                return Results.BadRequest(new { message = "Role was not found." });

            var isCurrentEmployee = TryGetEmployeeId(user, out var currentEmployeeId) && currentEmployeeId == employeeId;
            if (isCurrentEmployee && !request.IsActive)
                return Results.Conflict(new { message = "You cannot deactivate the account you are currently using." });

            if (isCurrentEmployee &&
                (!role.Permissions.Contains(Permissions.BackOfficeRead) || !role.Permissions.Contains(Permissions.EmployeesManage)))
            {
                return Results.Conflict(new
                {
                    message = "You cannot assign yourself a role without BackOffice and employee-management access."
                });
            }

            if (employee.IsActive && !request.IsActive)
            {
                var hasOpenShift = await db.Shifts.AnyAsync(
                    x => x.RestaurantId == restaurantId &&
                         x.OpenedByEmployeeId == employeeId &&
                         x.Status == ShiftStatus.Open,
                    ct);
                if (hasOpenShift)
                    return Results.Conflict(new { message = "The employee cannot be deactivated while they have an open shift." });
            }

            var pinChanged = !string.IsNullOrWhiteSpace(request.NewPin);
            if (pinChanged)
            {
                var pinError = ValidatePin(request.NewPin!);
                if (pinError is not null)
                    return Results.BadRequest(new { message = pinError });

                if (await PinExists(db, pinHasher, restaurantId, request.NewPin!, employeeId, ct))
                    return Results.Conflict(new { message = "This PIN is already used by another employee in this restaurant." });
            }

            employee.Name = name;
            employee.RoleId = role.Id;
            employee.IsActive = request.IsActive;
            if (pinChanged)
                employee.PinHash = pinHasher.Hash(restaurantId, request.NewPin!);

            AddAudit(db, user, restaurantId, "EMPLOYEE_UPDATED", "Employee", employee.Id, new
            {
                employee.Name,
                employee.RoleId,
                roleName = role.Name,
                employee.IsActive,
                pinChanged
            });
            await db.SaveChangesAsync(ct);

            return Results.Ok(ToEmployeeResponse(employee, role.Name));
        }).RequireAuthorization(Permissions.EmployeesManage);

        return app;
    }

    private static async Task<bool> PinExists(
        RestaurantDbContext db,
        PinHasher pinHasher,
        Guid restaurantId,
        string pin,
        Guid? excludeEmployeeId,
        CancellationToken ct)
    {
        var lookupPrefix = pinHasher.LookupPrefix(restaurantId, pin);
        var hashes = await db.Employees
            .AsNoTracking()
            .Where(x => x.RestaurantId == restaurantId &&
                        (!excludeEmployeeId.HasValue || x.Id != excludeEmployeeId.Value) &&
                        (x.PinHash.StartsWith(lookupPrefix) || !x.PinHash.StartsWith(PinHasher.LookupMarker)))
            .Select(x => x.PinHash)
            .ToListAsync(ct);

        return hashes.Any(hash => pinHasher.Verify(pin, hash));
    }

    private static string? ValidatePin(string pin)
    {
        if (pin.Length is < 4 or > 12)
            return "PIN must contain between 4 and 12 digits.";
        if (!pin.All(char.IsDigit))
            return "PIN must contain digits only.";
        return null;
    }

    private static string[] NormalizePermissions(string[]? requested, out string? invalidPermission)
    {
        invalidPermission = null;
        var permissions = (requested ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        invalidPermission = permissions.FirstOrDefault(x => !Permissions.All.Contains(x, StringComparer.Ordinal));
        return permissions;
    }

    private static object ToRoleResponse(Role role, int employeeCount, int activeEmployeeCount) => new
    {
        id = role.Id,
        name = role.Name,
        permissions = role.Permissions,
        employeeCount,
        activeEmployeeCount
    };

    private static object ToEmployeeResponse(Employee employee, string? roleName) => new
    {
        id = employee.Id,
        name = employee.Name,
        roleId = employee.RoleId,
        roleName,
        isActive = employee.IsActive,
        createdAt = employee.CreatedAt
    };

    private static bool TryGetRestaurantId(ClaimsPrincipal user, out Guid restaurantId) =>
        Guid.TryParse(user.FindFirstValue("restaurant_id"), out restaurantId);

    private static bool TryGetEmployeeId(ClaimsPrincipal user, out Guid employeeId) =>
        Guid.TryParse(user.FindFirstValue("employee_id"), out employeeId);

    private static string? NormalizeName(string? value, int maxLength)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) || normalized.Length > maxLength ? null : normalized;
    }

    private static void AddAudit(
        RestaurantDbContext db,
        ClaimsPrincipal user,
        Guid restaurantId,
        string eventType,
        string entityType,
        Guid entityId,
        object payload)
    {
        Guid? employeeId = Guid.TryParse(user.FindFirstValue("employee_id"), out var parsedEmployeeId)
            ? parsedEmployeeId
            : null;

        db.AuditEvents.Add(new AuditEvent
        {
            RestaurantId = restaurantId,
            EmployeeId = employeeId,
            EventType = eventType,
            EntityType = entityType,
            EntityId = entityId,
            PayloadJson = JsonSerializer.Serialize(payload)
        });
    }
}

public sealed record CreateRoleRequest(string Name, string[] Permissions);
public sealed record UpdateRoleRequest(string Name, string[] Permissions);
public sealed record CreateEmployeeRequest(string Name, Guid RoleId, string Pin);
public sealed record UpdateEmployeeRequest(string Name, Guid RoleId, bool IsActive, string? NewPin);
