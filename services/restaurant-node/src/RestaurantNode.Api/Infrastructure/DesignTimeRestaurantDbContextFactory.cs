using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace RestaurantNode.Api.Infrastructure;

public sealed class DesignTimeRestaurantDbContextFactory
    : IDesignTimeDbContextFactory<RestaurantDbContext>
{
    private const string DefaultConnectionString =
        "Host=127.0.0.1;Port=5432;Database=restaurant_local;" +
        "Username=restaurant;Password=restaurant_dev_password";

    public RestaurantDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__RestaurantDb") ??
            Environment.GetEnvironmentVariable("RESTAURANT_DB_CONNECTION") ??
            DefaultConnectionString;

        var options = new DbContextOptionsBuilder<RestaurantDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new RestaurantDbContext(options);
    }
}
