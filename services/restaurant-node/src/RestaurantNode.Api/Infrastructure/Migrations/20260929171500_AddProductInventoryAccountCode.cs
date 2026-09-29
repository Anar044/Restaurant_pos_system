using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RestaurantNode.Api.Infrastructure;

#nullable disable

namespace RestaurantNode.Api.Infrastructure.Migrations
{
    [DbContext(typeof(RestaurantDbContext))]
    [Migration("20260929171500_AddProductInventoryAccountCode")]
    public partial class AddProductInventoryAccountCode : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InventoryAccountCode",
                table: "products",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE products
                SET "InventoryAccountCode" = '201-1'
                WHERE "TrackStock" = TRUE
                  AND "InventoryAccountCode" IS NULL;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InventoryAccountCode",
                table: "products");
        }
    }
}
