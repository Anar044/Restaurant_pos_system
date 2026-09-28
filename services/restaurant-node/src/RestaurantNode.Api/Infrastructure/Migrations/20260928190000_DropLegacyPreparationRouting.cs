using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RestaurantNode.Api.Infrastructure;

#nullable disable

namespace RestaurantNode.Api.Infrastructure.Migrations
{
    [DbContext(typeof(RestaurantDbContext))]
    [Migration("20260928190000_DropLegacyPreparationRouting")]
    public partial class DropLegacyPreparationRouting : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "preparation_routes");
            migrationBuilder.DropTable(name: "sales_points");
            migrationBuilder.DropTable(name: "preparation_places");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
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
                        onDelete: ReferentialAction.Cascade);
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
                        name: "FK_preparation_routes_preparation_places_PreparationPlaceId",
                        column: x => x.PreparationPlaceId,
                        principalTable: "preparation_places",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_preparation_routes_preparation_place_types_PreparationPlaceTypeId",
                        column: x => x.PreparationPlaceTypeId,
                        principalTable: "preparation_place_types",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_preparation_routes_sales_points_SalesPointId",
                        column: x => x.SalesPointId,
                        principalTable: "sales_points",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(name: "IX_preparation_places_PreparationPlaceTypeId", table: "preparation_places", column: "PreparationPlaceTypeId");
            migrationBuilder.CreateIndex(name: "IX_preparation_places_PrinterId", table: "preparation_places", column: "PrinterId");
            migrationBuilder.CreateIndex(name: "IX_preparation_places_WarehouseId", table: "preparation_places", column: "WarehouseId");
            migrationBuilder.CreateIndex(name: "IX_preparation_places_RestaurantId_Name", table: "preparation_places", columns: new[] { "RestaurantId", "Name" });
            migrationBuilder.CreateIndex(name: "IX_sales_points_HallId", table: "sales_points", column: "HallId");
            migrationBuilder.CreateIndex(name: "IX_sales_points_RestaurantId_Name", table: "sales_points", columns: new[] { "RestaurantId", "Name" });
            migrationBuilder.CreateIndex(name: "IX_preparation_routes_PreparationPlaceId", table: "preparation_routes", column: "PreparationPlaceId");
            migrationBuilder.CreateIndex(name: "IX_preparation_routes_PreparationPlaceTypeId", table: "preparation_routes", column: "PreparationPlaceTypeId");
            migrationBuilder.CreateIndex(name: "IX_preparation_routes_SalesPointId", table: "preparation_routes", column: "SalesPointId");
            migrationBuilder.CreateIndex(
                name: "IX_preparation_routes_RestaurantId_SalesPointId_PreparationPlaceTypeId",
                table: "preparation_routes",
                columns: new[] { "RestaurantId", "SalesPointId", "PreparationPlaceTypeId" },
                unique: true);
        }
    }
}
