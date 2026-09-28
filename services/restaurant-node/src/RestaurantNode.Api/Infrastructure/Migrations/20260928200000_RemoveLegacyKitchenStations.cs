using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RestaurantNode.Api.Infrastructure;

#nullable disable

namespace RestaurantNode.Api.Infrastructure.Migrations
{
    [DbContext(typeof(RestaurantDbContext))]
    [Migration("20260928200000_RemoveLegacyKitchenStations")]
    public partial class RemoveLegacyKitchenStations : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Be defensive with legacy databases: make sure every old kitchen station
            // has a matching department before kitchen tickets begin referencing departments.
            migrationBuilder.Sql(@"
INSERT INTO restaurant_departments
    (""Id"", ""RestaurantId"", ""GroupId"", ""Name"", ""HallId"", ""WarehouseId"", ""PrinterId"", ""IsActive"")
SELECT
    ks.""Id"",
    ks.""RestaurantId"",
    ks.""RestaurantId"",
    ks.""Name"",
    NULL,
    ks.""WarehouseId"",
    ks.""PrinterId"",
    ks.""IsActive""
FROM kitchen_stations ks
WHERE EXISTS (
    SELECT 1
    FROM restaurant_groups g
    WHERE g.""Id"" = ks.""RestaurantId""
)
AND NOT EXISTS (
    SELECT 1
    FROM restaurant_departments d
    WHERE d.""Id"" = ks.""Id""
);
");

            migrationBuilder.Sql(@"
ALTER TABLE products
DROP CONSTRAINT IF EXISTS ""FK_products_kitchen_stations_KitchenStationId"";

DROP INDEX IF EXISTS ""IX_products_KitchenStationId"";
");

            migrationBuilder.Sql(@"
ALTER TABLE products
DROP COLUMN IF EXISTS ""KitchenStationId"";
");

            migrationBuilder.Sql(@"
ALTER TABLE kitchen_tickets
DROP CONSTRAINT IF EXISTS ""FK_kitchen_tickets_kitchen_stations_KitchenStationId"";
");

            migrationBuilder.RenameColumn(
                name: "KitchenStationId",
                table: "kitchen_tickets",
                newName: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_kitchen_tickets_RestaurantId_DepartmentId_CreatedAt",
                table: "kitchen_tickets",
                columns: new[] { "RestaurantId", "DepartmentId", "CreatedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_kitchen_tickets_restaurant_departments_DepartmentId",
                table: "kitchen_tickets",
                column: "DepartmentId",
                principalTable: "restaurant_departments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql(@"
DROP TABLE IF EXISTS kitchen_stations CASCADE;
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_kitchen_tickets_restaurant_departments_DepartmentId",
                table: "kitchen_tickets");

            migrationBuilder.DropIndex(
                name: "IX_kitchen_tickets_RestaurantId_DepartmentId_CreatedAt",
                table: "kitchen_tickets");

            migrationBuilder.RenameColumn(
                name: "DepartmentId",
                table: "kitchen_tickets",
                newName: "KitchenStationId");

            migrationBuilder.CreateTable(
                name: "kitchen_stations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    PrinterId = table.Column<Guid>(type: "uuid", nullable: true),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_kitchen_stations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_kitchen_stations_printers_PrinterId",
                        column: x => x.PrinterId,
                        principalTable: "printers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_kitchen_stations_warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.Sql(@"
INSERT INTO kitchen_stations
    (""Id"", ""RestaurantId"", ""Name"", ""PrinterId"", ""WarehouseId"", ""IsActive"")
SELECT
    d.""Id"",
    d.""RestaurantId"",
    d.""Name"",
    d.""PrinterId"",
    d.""WarehouseId"",
    d.""IsActive""
FROM restaurant_departments d
WHERE d.""HallId"" IS NULL;
");

            migrationBuilder.CreateIndex(
                name: "IX_kitchen_stations_PrinterId",
                table: "kitchen_stations",
                column: "PrinterId");

            migrationBuilder.CreateIndex(
                name: "IX_kitchen_stations_WarehouseId",
                table: "kitchen_stations",
                column: "WarehouseId");

            migrationBuilder.AddColumn<Guid>(
                name: "KitchenStationId",
                table: "products",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(@"
UPDATE products p
SET ""KitchenStationId"" = p.""PreparationPlaceTypeId""
WHERE p.""PreparationPlaceTypeId"" IS NOT NULL
  AND EXISTS (
      SELECT 1 FROM kitchen_stations ks
      WHERE ks.""Id"" = p.""PreparationPlaceTypeId""
  );
");

            migrationBuilder.CreateIndex(
                name: "IX_products_KitchenStationId",
                table: "products",
                column: "KitchenStationId");

            migrationBuilder.AddForeignKey(
                name: "FK_products_kitchen_stations_KitchenStationId",
                table: "products",
                column: "KitchenStationId",
                principalTable: "kitchen_stations",
                principalColumn: "Id");
        }
    }
}
