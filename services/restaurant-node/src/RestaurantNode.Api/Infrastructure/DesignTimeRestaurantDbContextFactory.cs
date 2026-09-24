using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace RestaurantNode.Api.Infrastructure;

public sealed class DesignTimeRestaurantDbContextFactory
    : IDesignTimeDbContextFactory<RestaurantDbContext>
{
    public RestaurantDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<RestaurantDbContext>()
            .UseNpgsql(
                "Host=127.0.0.1;Port=5432;Database=restaurant_design_time;" +
                "Username=restaurant;Password=restaurant_design_time")
            .Options;

        return new RestaurantDbContext(options);
    }
}
