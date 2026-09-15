using Microsoft.EntityFrameworkCore;
using RestaurantNode.Api.Domain;
using RestaurantNode.Api.Security;

namespace RestaurantNode.Api.Infrastructure;

public static class SeedData
{
    public static readonly Guid RestaurantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid AdminRoleId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid AdminEmployeeId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    public static async Task EnsureDevelopmentSeedAsync(
        IServiceProvider services,
        RestaurantDbContext db,
        IHostEnvironment environment)
    {
        if (!environment.IsDevelopment()) return;
        if (await db.Restaurants.AnyAsync(x => x.Id == RestaurantId)) return;

        var pinHasher = services.GetRequiredService<PinHasher>();
        var configuration = services.GetRequiredService<IConfiguration>();
        var pin = configuration["Seed:AdminPin"] ?? "1234";

        var restaurant = new Restaurant
        {
            Id = RestaurantId,
            Name = "Demo Restaurant",
            CurrencyCode = "AZN",
            TimeZone = "Asia/Baku"
        };
        var adminRole = new Role
        {
            Id = AdminRoleId,
            RestaurantId = RestaurantId,
            Name = "Administrator",
            Permissions = ["orders.read", "orders.write", "orders.void", "menu.read", "shifts.manage", "payments.write"]
        };
        var admin = new Employee
        {
            Id = AdminEmployeeId,
            RestaurantId = RestaurantId,
            RoleId = AdminRoleId,
            Name = "Administrator",
            PinHash = pinHasher.Hash(pin)
        };

        var hall = new Hall { RestaurantId = RestaurantId, Name = "Main Hall", SortOrder = 1 };
        var tables = Enumerable.Range(1, 8)
            .Select(i => new DiningTable { RestaurantId = RestaurantId, HallId = hall.Id, Name = i.ToString(), Seats = 4, SortOrder = i })
            .ToList();

        var hot = new KitchenStation { RestaurantId = RestaurantId, Name = "Hot Kitchen" };
        var bar = new KitchenStation { RestaurantId = RestaurantId, Name = "Bar" };
        var foodCategory = new Category { RestaurantId = RestaurantId, Name = "Food", SortOrder = 1 };
        var drinksCategory = new Category { RestaurantId = RestaurantId, Name = "Drinks", SortOrder = 2 };

        var burger = new Product { RestaurantId = RestaurantId, CategoryId = foodCategory.Id, KitchenStationId = hot.Id, Name = "Burger", SortOrder = 1 };
        var pasta = new Product { RestaurantId = RestaurantId, CategoryId = foodCategory.Id, KitchenStationId = hot.Id, Name = "Pasta", SortOrder = 2 };
        var cola = new Product { RestaurantId = RestaurantId, CategoryId = drinksCategory.Id, KitchenStationId = bar.Id, Name = "Cola", SortOrder = 1 };
        var water = new Product { RestaurantId = RestaurantId, CategoryId = drinksCategory.Id, KitchenStationId = bar.Id, Name = "Water", SortOrder = 2 };

        db.AddRange(restaurant, adminRole, admin, hall, hot, bar, foodCategory, drinksCategory);
        db.AddRange(tables);
        db.AddRange(burger, pasta, cola, water);
        db.AddRange(
            new ProductPrice { RestaurantId = RestaurantId, ProductId = burger.Id, Amount = 12.00m, CurrencyCode = "AZN" },
            new ProductPrice { RestaurantId = RestaurantId, ProductId = pasta.Id, Amount = 10.00m, CurrencyCode = "AZN" },
            new ProductPrice { RestaurantId = RestaurantId, ProductId = cola.Id, Amount = 3.00m, CurrencyCode = "AZN" },
            new ProductPrice { RestaurantId = RestaurantId, ProductId = water.Id, Amount = 2.00m, CurrencyCode = "AZN" }
        );

        await db.SaveChangesAsync();
    }
}
