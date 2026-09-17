using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using RestaurantNode.Api.Domain;

namespace RestaurantNode.Api.Security;

public sealed class JwtTokenService(IConfiguration configuration)
{
    public (string Token, DateTimeOffset ExpiresAt) Create(Employee employee, Role role, Guid organizationId)
    {
        var key = configuration["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key is required.");
        var issuer = configuration["Jwt:Issuer"] ?? "restaurant-node";
        var audience = configuration["Jwt:Audience"] ?? "restaurant-pos";
        var expiresAt = DateTimeOffset.UtcNow.AddHours(12);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, employee.Id.ToString()),
            new("employee_id", employee.Id.ToString()),
            new("organization_id", organizationId.ToString()),
            new("restaurant_id", employee.RestaurantId.ToString()),
            new(ClaimTypes.Name, employee.Name),
            new(ClaimTypes.Role, role.Name)
        };
        claims.AddRange(role.Permissions.Select(p => new Claim("permission", p)));

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
            SecurityAlgorithms.HmacSha256);

        var jwt = new JwtSecurityToken(
            issuer,
            audience,
            claims,
            notBefore: DateTime.UtcNow,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        return (new JwtSecurityTokenHandler().WriteToken(jwt), expiresAt);
    }
}
