using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RestaurantNode.Api.Infrastructure;

#nullable disable

namespace RestaurantNode.Api.Infrastructure.Migrations
{
    [DbContext(typeof(RestaurantDbContext))]
    [Migration("20260928180000_AddRestaurantGroupsAndDepartments")]
    public partial class AddRestaurantGroupsAndDepartments : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "restaurant_groups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_restaurant_groups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "restaurant_group_devices",
                columns: table => new
                {
                    GroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsMainCashRegister = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_restaurant_group_devices", x => new { x.GroupId, x.DeviceId });
                    table.ForeignKey(
                        name: "FK_restaurant_group_devices_devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_restaurant_group_devices_restaurant_groups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "restaurant_groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "restaurant_departments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    HallId = table.Column<Guid>(type: "uuid", nullable: true),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: true),
                    PrinterId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_restaurant_departments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_restaurant_departments_halls_HallId",
                        column: x => x.HallId,
                        principalTable: "halls",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_restaurant_departments_printers_PrinterId",
                        column: x => x.PrinterId,
                        principalTable: "printers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_restaurant_departments_restaurant_groups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "restaurant_groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_restaurant_departments_warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "group_preparation_maps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    PreparationPlaceTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_group_preparation_maps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_group_preparation_maps_preparation_place_types_PreparationPlaceTypeId",
                        column: x => x.PreparationPlaceTypeId,
                        principalTable: "preparation_place_types",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_group_preparation_maps_restaurant_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "restaurant_departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_group_preparation_maps_restaurant_groups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "restaurant_groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_restaurant_groups_RestaurantId_Name",
                table: "restaurant_groups",
                columns: new[] { "RestaurantId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_restaurant_group_devices_DeviceId",
                table: "restaurant_group_devices",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_restaurant_departments_GroupId_Name",
                table: "restaurant_departments",
                columns: new[] { "GroupId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_restaurant_departments_HallId",
                table: "restaurant_departments",
                column: "HallId");

            migrationBuilder.CreateIndex(
                name: "IX_restaurant_departments_PrinterId",
                table: "restaurant_departments",
                column: "PrinterId");

            migrationBuilder.CreateIndex(
                name: "IX_restaurant_departments_WarehouseId",
                table: "restaurant_departments",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_group_preparation_maps_DepartmentId",
                table: "group_preparation_maps",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_group_preparation_maps_PreparationPlaceTypeId",
                table: "group_preparation_maps",
                column: "PreparationPlaceTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_group_preparation_maps_GroupId_PreparationPlaceTypeId",
                table: "group_preparation_maps",
                columns: new[] { "GroupId", "PreparationPlaceTypeId" },
                unique: true);

            migrationBuilder.Sql(@"
INSERT INTO restaurant_groups (""Id"", ""RestaurantId"", ""Name"", ""IsActive"")
SELECT r.""Id"", r.""Id"", 'Основная группа', TRUE
FROM restaurants r
WHERE NOT EXISTS (
    SELECT 1 FROM restaurant_groups g WHERE g.""RestaurantId"" = r.""Id""
);

INSERT INTO restaurant_group_devices (""GroupId"", ""DeviceId"", ""IsMainCashRegister"")
SELECT
    d.""RestaurantId"",
    d.""Id"",
    CASE
        WHEN d.""Id"" = (
            SELECT d2.""Id""
            FROM devices d2
            WHERE d2.""RestaurantId"" = d.""RestaurantId""
              AND d2.""IsActive"" = TRUE
              AND d2.""Type"" = 'Pos'
            ORDER BY d2.""Name"", d2.""Id""
            LIMIT 1
        ) THEN TRUE
        ELSE FALSE
    END
FROM devices d
WHERE d.""IsActive"" = TRUE
  AND NOT EXISTS (
      SELECT 1
      FROM restaurant_group_devices gd
      WHERE gd.""GroupId"" = d.""RestaurantId""
        AND gd.""DeviceId"" = d.""Id""
  );

INSERT INTO restaurant_departments
    (""Id"", ""RestaurantId"", ""GroupId"", ""Name"", ""HallId"", ""WarehouseId"", ""PrinterId"", ""IsActive"")
SELECT
    h.""Id"", h.""RestaurantId"", h.""RestaurantId"", h.""Name"", h.""Id"", NULL, NULL, h.""IsActive""
FROM halls h
WHERE NOT EXISTS (
    SELECT 1 FROM restaurant_departments d WHERE d.""Id"" = h.""Id""
);

INSERT INTO restaurant_departments
    (""Id"", ""RestaurantId"", ""GroupId"", ""Name"", ""HallId"", ""WarehouseId"", ""PrinterId"", ""IsActive"")
SELECT
    p.""Id"", p.""RestaurantId"", p.""RestaurantId"", p.""Name"", NULL, p.""WarehouseId"", p.""PrinterId"", p.""IsActive""
FROM preparation_places p
WHERE NOT EXISTS (
    SELECT 1 FROM restaurant_departments d WHERE d.""Id"" = p.""Id""
);

INSERT INTO group_preparation_maps
    (""Id"", ""RestaurantId"", ""GroupId"", ""PreparationPlaceTypeId"", ""DepartmentId"", ""IsActive"")
SELECT DISTINCT ON (p.""RestaurantId"", p.""PreparationPlaceTypeId"")
    p.""PreparationPlaceTypeId"",
    p.""RestaurantId"",
    p.""RestaurantId"",
    p.""PreparationPlaceTypeId"",
    p.""Id"",
    p.""IsActive""
FROM preparation_places p
WHERE EXISTS (
    SELECT 1 FROM restaurant_departments d WHERE d.""Id"" = p.""Id""
)
AND NOT EXISTS (
    SELECT 1
    FROM group_preparation_maps m
    WHERE m.""GroupId"" = p.""RestaurantId""
      AND m.""PreparationPlaceTypeId"" = p.""PreparationPlaceTypeId""
)
ORDER BY p.""RestaurantId"", p.""PreparationPlaceTypeId"", p.""IsActive"" DESC, p.""Name"";
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "group_preparation_maps");
            migrationBuilder.DropTable(name: "restaurant_group_devices");
            migrationBuilder.DropTable(name: "restaurant_departments");
            migrationBuilder.DropTable(name: "restaurant_groups");
        }
    }
}
