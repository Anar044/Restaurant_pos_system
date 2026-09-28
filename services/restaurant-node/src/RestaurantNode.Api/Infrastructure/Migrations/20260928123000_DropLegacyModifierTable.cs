using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RestaurantNode.Api.Infrastructure;

#nullable disable

namespace RestaurantNode.Api.Infrastructure.Migrations
{
    [DbContext(typeof(RestaurantDbContext))]
    [Migration("20260928123000_DropLegacyModifierTable")]
    public partial class DropLegacyModifierTable : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "modifiers");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "modifiers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    PriceDelta = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_modifiers", x => x.Id);
                });

            migrationBuilder.Sql(@"
INSERT INTO modifiers (""Id"", ""RestaurantId"", ""Name"", ""PriceDelta"", ""IsActive"")
SELECT
    p.""Id"",
    p.""RestaurantId"",
    p.""Name"",
    COALESCE((
        SELECT pp.""Amount""
        FROM product_prices pp
        WHERE pp.""ProductId"" = p.""Id""
          AND pp.""ValidTo"" IS NULL
        ORDER BY pp.""ValidFrom"" DESC
        LIMIT 1
    ), 0),
    p.""IsActive""
FROM products p
WHERE p.""Type"" = 'MODIFIER';
");
        }
    }
}
