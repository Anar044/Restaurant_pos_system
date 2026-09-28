using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RestaurantNode.Api.Infrastructure;

#nullable disable

namespace RestaurantNode.Api.Infrastructure.Migrations
{
    [DbContext(typeof(RestaurantDbContext))]
    [Migration("20260928121000_MigrateModifiersToNomenclature")]
    public partial class MigrateModifiersToNomenclature : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
INSERT INTO products
    (""Id"", ""RestaurantId"", ""CategoryId"", ""KitchenStationId"", ""Name"", ""Sku"",
     ""Type"", ""Unit"", ""MinStock"", ""TrackStock"", ""IsSellable"", ""IsActive"", ""SortOrder"")
SELECT
    m.""Id"", m.""RestaurantId"", NULL, NULL, m.""Name"", NULL,
    'MODIFIER', 'pcs', 0, FALSE, TRUE, m.""IsActive"", 0
FROM modifiers m
WHERE NOT EXISTS (
    SELECT 1 FROM products p WHERE p.""Id"" = m.""Id""
);

INSERT INTO product_prices
    (""Id"", ""RestaurantId"", ""ProductId"", ""Amount"", ""CurrencyCode"", ""ValidFrom"", ""ValidTo"")
SELECT
    m.""Id"", m.""RestaurantId"", m.""Id"", m.""PriceDelta"", r.""CurrencyCode"", NOW(), NULL
FROM modifiers m
JOIN restaurants r ON r.""Id"" = m.""RestaurantId""
WHERE NOT EXISTS (
    SELECT 1 FROM product_prices pp
    WHERE pp.""ProductId"" = m.""Id"" AND pp.""ValidTo"" IS NULL
);
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DELETE FROM product_prices
WHERE ""ProductId"" IN (SELECT ""Id"" FROM modifiers);

DELETE FROM products
WHERE ""Type"" = 'MODIFIER'
  AND ""Id"" IN (SELECT ""Id"" FROM modifiers);
");
        }
    }
}
