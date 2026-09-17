using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using RestaurantNode.Api.Infrastructure;
using RestaurantNode.Api.Security;

namespace RestaurantNode.Api.Features.Auth;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/auth/pin", async (
            PinLoginRequest request,
            RestaurantDbContext db,
            PinHasher pinHasher,
            JwtTokenService jwt,
            CancellationToken ct) =>
        {
            if (request.RestaurantId == Guid.Empty || string.IsNullOrWhiteSpace(request.Pin) || request.Pin.Length is < 4 or > 12)
                return Results.BadRequest(new { message = "Invalid restaurant or PIN format." });

            var restaurant = await db.Restaurants
                .AsNoTracking()
                .Where(x => x.Id == request.RestaurantId && x.IsActive)
                .Select(x => new { x.Id, x.OrganizationId })
                .FirstOrDefaultAsync(ct);

            if (restaurant is null)
                return Results.Unauthorized();

            var lookupPrefix = pinHasher.LookupPrefix(request.RestaurantId, request.Pin);
            var employees = await db.Employees
                .AsNoTracking()
                .Include(x => x.Role)
                .Where(x => x.RestaurantId == request.RestaurantId &&
                            x.IsActive &&
                            (x.PinHash.StartsWith(lookupPrefix) || !x.PinHash.StartsWith(PinHasher.LookupMarker)))
                .ToListAsync(ct);

            var employee = employees.FirstOrDefault(x => pinHasher.Verify(request.Pin, x.PinHash));
            if (employee?.Role is null)
                return Results.Unauthorized();

            var (token, expiresAt) = jwt.Create(employee, employee.Role, restaurant.OrganizationId);
            return Results.Ok(new PinLoginResponse(
                token,
                expiresAt,
                employee.Id,
                employee.Name,
                employee.Role.Name,
                restaurant.OrganizationId,
                employee.RestaurantId));
        }).RequireRateLimiting("pin-login");

        app.MapGet("/api/v1/me", (ClaimsPrincipal user) =>
        {
            return Results.Ok(new
            {
                employeeId = user.FindFirstValue("employee_id"),
                organizationId = user.FindFirstValue("organization_id"),
                restaurantId = user.FindFirstValue("restaurant_id"),
                name = user.Identity?.Name,
                role = user.FindFirstValue(ClaimTypes.Role),
                permissions = user.FindAll("permission").Select(x => x.Value).ToArray()
            });
        }).RequireAuthorization();

        return app;
    }
}

public sealed record PinLoginRequest(Guid RestaurantId, string Pin);
public sealed record PinLoginResponse(
    string Token,
    DateTimeOffset ExpiresAt,
    Guid EmployeeId,
    string EmployeeName,
    string RoleName,
    Guid OrganizationId,
    Guid RestaurantId);
