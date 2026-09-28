using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RestaurantNode.Api.Infrastructure;

#nullable disable

namespace RestaurantNode.Api.Infrastructure.Migrations
{
    [DbContext(typeof(RestaurantDbContext))]
    [Migration("20260928210000_AlignGroupsDepartmentsAndHalls")]
    public partial class AlignGroupsDepartmentsAndHalls : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PreparationPlaceTypeId",
                table: "restaurant_departments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "GroupId",
                table: "halls",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PrecheckPrinterId",
                table: "halls",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE restaurant_departments d
                SET "PreparationPlaceTypeId" = m."PreparationPlaceTypeId"
                FROM group_preparation_maps m
                WHERE m."DepartmentId" = d."Id"
                  AND m."GroupId" = d."GroupId"
                  AND m."IsActive" = TRUE
                  AND d."PreparationPlaceTypeId" IS NULL;
                """);

            migrationBuilder.Sql("""
                UPDATE halls h
                SET "GroupId" = d."GroupId"
                FROM restaurant_departments d
                WHERE d."HallId" = h."Id"
                  AND d."RestaurantId" = h."RestaurantId"
                  AND h."GroupId" IS NULL;
                """);

            migrationBuilder.Sql("""
                UPDATE halls h
                SET "GroupId" = COALESCE(
                    (
                        SELECT g."Id"
                        FROM restaurant_groups g
                        WHERE g."RestaurantId" = h."RestaurantId"
                          AND g."IsActive" = TRUE
                        ORDER BY CASE WHEN g."Id" = h."RestaurantId" THEN 0 ELSE 1 END, g."Name"
                        LIMIT 1
                    ),
                    h."RestaurantId"
                )
                WHERE h."GroupId" IS NULL;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "GroupId",
                table: "halls",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.Sql("""
                ALTER TABLE restaurant_departments
                DROP CONSTRAINT IF EXISTS "FK_restaurant_departments_halls_HallId";
                DROP INDEX IF EXISTS "IX_restaurant_departments_HallId";
                ALTER TABLE restaurant_departments DROP COLUMN IF EXISTS "HallId";
                """);

            migrationBuilder.DropTable(
                name: "group_preparation_maps");

            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_halls_RestaurantId_Name\";");

            migrationBuilder.CreateIndex(
                name: "IX_halls_RestaurantId",
                table: "halls",
                column: "RestaurantId");

            migrationBuilder.CreateIndex(
                name: "IX_halls_GroupId_Name",
                table: "halls",
                columns: new[] { "GroupId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_halls_PrecheckPrinterId",
                table: "halls",
                column: "PrecheckPrinterId");

            migrationBuilder.CreateIndex(
                name: "IX_restaurant_departments_PreparationPlaceTypeId",
                table: "restaurant_departments",
                column: "PreparationPlaceTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_restaurant_departments_GroupId_PreparationPlaceTypeId",
                table: "restaurant_departments",
                columns: new[] { "GroupId", "PreparationPlaceTypeId" },
                unique: true,
                filter: "\"PreparationPlaceTypeId\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_halls_printers_PrecheckPrinterId",
                table: "halls",
                column: "PrecheckPrinterId",
                principalTable: "printers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_halls_restaurant_groups_GroupId",
                table: "halls",
                column: "GroupId",
                principalTable: "restaurant_groups",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_restaurant_departments_preparation_place_types_PreparationPlaceTypeId",
                table: "restaurant_departments",
                column: "PreparationPlaceTypeId",
                principalTable: "preparation_place_types",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_halls_printers_PrecheckPrinterId",
                table: "halls");

            migrationBuilder.DropForeignKey(
                name: "FK_halls_restaurant_groups_GroupId",
                table: "halls");

            migrationBuilder.DropForeignKey(
                name: "FK_restaurant_departments_preparation_place_types_PreparationPlaceTypeId",
                table: "restaurant_departments");

            migrationBuilder.DropIndex(
                name: "IX_halls_RestaurantId",
                table: "halls");

            migrationBuilder.DropIndex(
                name: "IX_halls_GroupId_Name",
                table: "halls");

            migrationBuilder.DropIndex(
                name: "IX_halls_PrecheckPrinterId",
                table: "halls");

            migrationBuilder.DropIndex(
                name: "IX_restaurant_departments_PreparationPlaceTypeId",
                table: "restaurant_departments");

            migrationBuilder.DropIndex(
                name: "IX_restaurant_departments_GroupId_PreparationPlaceTypeId",
                table: "restaurant_departments");

            migrationBuilder.AddColumn<Guid>(
                name: "HallId",
                table: "restaurant_departments",
                type: "uuid",
                nullable: true);

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
                    table.ForeignKey("FK_group_preparation_maps_preparation_place_types_PreparationPlaceTypeId", x => x.PreparationPlaceTypeId, "preparation_place_types", "Id", onDelete: ReferentialAction.Cascade);
                    table.ForeignKey("FK_group_preparation_maps_restaurant_departments_DepartmentId", x => x.DepartmentId, "restaurant_departments", "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey("FK_group_preparation_maps_restaurant_groups_GroupId", x => x.GroupId, "restaurant_groups", "Id", onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql("""
                INSERT INTO group_preparation_maps
                    ("Id", "RestaurantId", "GroupId", "PreparationPlaceTypeId", "DepartmentId", "IsActive")
                SELECT gen_random_uuid(), d."RestaurantId", d."GroupId", d."PreparationPlaceTypeId", d."Id", TRUE
                FROM restaurant_departments d
                WHERE d."PreparationPlaceTypeId" IS NOT NULL;
                """);

            migrationBuilder.DropColumn(
                name: "PreparationPlaceTypeId",
                table: "restaurant_departments");

            migrationBuilder.DropColumn(
                name: "PrecheckPrinterId",
                table: "halls");

            migrationBuilder.DropColumn(
                name: "GroupId",
                table: "halls");
        }
    }
}
