using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RestaurantNode.Api.Infrastructure;

#nullable disable

namespace RestaurantNode.Api.Infrastructure.Migrations
{
    [DbContext(typeof(RestaurantDbContext))]
    [Migration("20260928170000_AddPreparationRoutingModel")]
    public partial class AddPreparationRoutingModel : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "preparation_place_types",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_preparation_place_types", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "sales_points",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false),
                    HallId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales_points", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sales_points_halls_HallId",
                        column: x => x.HallId,
                        principalTable: "halls",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "preparation_places",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    PreparationPlaceTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    PrinterId = table.Column<Guid>(type: "uuid", nullable: true),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_preparation_places", x => x.Id);
                    table.ForeignKey(
                        name: "FK_preparation_places_preparation_place_types_PreparationPlaceTypeId",
                        column: x => x.PreparationPlaceTypeId,
                        principalTable: "preparation_place_types",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_preparation_places_printers_PrinterId",
                        column: x => x.PrinterId,
                        principalTable: "printers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_preparation_places_warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "preparation_routes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SalesPointId = table.Column<Guid>(type: "uuid", nullable: false),
                    PreparationPlaceTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    PreparationPlaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_preparation_routes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_preparation_routes_preparation_place_types_PreparationPlaceTypeId",
                        column: x => x.PreparationPlaceTypeId,
                        principalTable: "preparation_place_types",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_preparation_routes_preparation_places_PreparationPlaceId",
                        column: x => x.PreparationPlaceId,
                        principalTable: "preparation_places",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_preparation_routes_sales_points_SalesPointId",
                        column: x => x.SalesPointId,
                        principalTable: "sales_points",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddColumn<Guid>(
                name: "PreparationPlaceTypeId",
                table: "products",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(@"
INSERT INTO preparation_place_types (""Id"", ""RestaurantId"", ""Name"", ""IsActive"")
SELECT ""Id"", ""RestaurantId"", ""Name"", ""IsActive""
FROM kitchen_stations
ON CONFLICT (""Id"") DO NOTHING;

INSERT INTO preparation_places
    (""Id"", ""RestaurantId"", ""PreparationPlaceTypeId"", ""Name"", ""PrinterId"", ""WarehouseId"", ""IsActive"")
SELECT
    ""Id"", ""RestaurantId"", ""Id"", ""Name"", ""PrinterId"", ""WarehouseId"", ""IsActive""
FROM kitchen_stations
ON CONFLICT (""Id"") DO NOTHING;

UPDATE products
SET ""PreparationPlaceTypeId"" = ""KitchenStationId""
WHERE ""KitchenStationId"" IS NOT NULL
  AND ""PreparationPlaceTypeId"" IS NULL;
");

            migrationBuilder.CreateIndex(
                name: "IX_products_PreparationPlaceTypeId",
                table: "products",
                column: "PreparationPlaceTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_preparation_place_types_RestaurantId_Name",
                table: "preparation_place_types",
                columns: new[] { "RestaurantId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_preparation_places_PreparationPlaceTypeId",
                table: "preparation_places",
                column: "PreparationPlaceTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_preparation_places_PrinterId",
                table: "preparation_places",
                column: "PrinterId");

            migrationBuilder.CreateIndex(
                name: "IX_preparation_places_WarehouseId",
                table: "preparation_places",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_preparation_places_RestaurantId_Name",
                table: "preparation_places",
                columns: new[] { "RestaurantId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sales_points_HallId",
                table: "sales_points",
                column: "HallId");

            migrationBuilder.CreateIndex(
                name: "IX_sales_points_RestaurantId_Name",
                table: "sales_points",
                columns: new[] { "RestaurantId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_preparation_routes_PreparationPlaceId",
                table: "preparation_routes",
                column: "PreparationPlaceId");

            migrationBuilder.CreateIndex(
                name: "IX_preparation_routes_PreparationPlaceTypeId",
                table: "preparation_routes",
                column: "PreparationPlaceTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_preparation_routes_SalesPointId",
                table: "preparation_routes",
                column: "SalesPointId");

            migrationBuilder.CreateIndex(
                name: "IX_preparation_routes_RestaurantId_SalesPointId_PreparationPlaceTypeId",
                table: "preparation_routes",
                columns: new[] { "RestaurantId", "SalesPointId", "PreparationPlaceTypeId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_products_preparation_place_types_PreparationPlaceTypeId",
                table: "products",
                column: "PreparationPlaceTypeId",
                principalTable: "preparation_place_types",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_products_preparation_place_types_PreparationPlaceTypeId",
                table: "products");

            migrationBuilder.DropTable(name: "preparation_routes");
            migrationBuilder.DropTable(name: "preparation_places");
            migrationBuilder.DropTable(name: "sales_points");
            migrationBuilder.DropTable(name: "preparation_place_types");

            migrationBuilder.DropIndex(
                name: "IX_products_PreparationPlaceTypeId",
                table: "products");

            migrationBuilder.DropColumn(
                name: "PreparationPlaceTypeId",
                table: "products");
        }
    }
}
