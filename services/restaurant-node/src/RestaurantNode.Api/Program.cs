using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using RestaurantNode.Api.Features.Auth;
using RestaurantNode.Api.Features.BackOffice;
using RestaurantNode.Api.Features.Halls;
using RestaurantNode.Api.Features.Menu;
using RestaurantNode.Api.Features.Orders;
using RestaurantNode.Api.Infrastructure;
using RestaurantNode.Api.Security;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("RestaurantDb")
    ?? throw new InvalidOperationException("ConnectionStrings:RestaurantDb is required.");

builder.Services.AddDbContext<RestaurantDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddScoped<PinHasher>();
builder.Services.AddScoped<JwtTokenService>();

var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("Jwt:Key is required.");
if (Encoding.UTF8.GetByteCount(jwtKey) < 32)
    throw new InvalidOperationException("Jwt:Key must be at least 32 bytes.");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "restaurant-node",
            ValidAudience = builder.Configuration["Jwt:Audience"] ?? "restaurant-pos",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ClockSkew = TimeSpan.FromSeconds(15)
        };
    });

builder.Services.AddAuthorization(options =>
{
    foreach (var permission in Permissions.All)
        options.AddPolicy(permission, policy => policy.RequireClaim("permission", permission));
});
builder.Services.AddCors(options =>
{
    options.AddPolicy("dev-pos", policy => policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("pin-login", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});

var app = builder.Build();

if (app.Environment.IsDevelopment()) app.UseCors("dev-pos");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "restaurant-node", utc = DateTimeOffset.UtcNow }));

app.MapAuthEndpoints();
app.MapBackOfficeEndpoints();
app.MapBackOfficeHallEndpoints();
app.MapBackOfficeMenuEndpoints();
app.MapBackOfficeKitchenEndpoints();
app.MapHallEndpoints();
app.MapMenuEndpoints();
app.MapOrderEndpoints();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
    await db.Database.MigrateAsync();
    await SeedData.EnsureDevelopmentSeedAsync(scope.ServiceProvider, db, app.Environment);
}

app.Run();
