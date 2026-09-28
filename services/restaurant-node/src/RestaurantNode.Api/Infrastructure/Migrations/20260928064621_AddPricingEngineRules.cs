using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantNode.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPricingEngineRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CategoryIdSnapshot",
                table: "order_items",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "ApplicationModeSnapshot",
                table: "order_adjustments",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "CanStackSnapshot",
                table: "order_adjustments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid[]>(
                name: "CategoryIdsSnapshot",
                table: "order_adjustments",
                type: "uuid[]",
                nullable: false,
                defaultValue: new Guid[0]);

            migrationBuilder.AddColumn<int>(
                name: "EndMinuteSnapshot",
                table: "order_adjustments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PrioritySnapshot",
                table: "order_adjustments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid[]>(
                name: "ProductIdsSnapshot",
                table: "order_adjustments",
                type: "uuid[]",
                nullable: false,
                defaultValue: new Guid[0]);

            migrationBuilder.AddColumn<int>(
                name: "StartMinuteSnapshot",
                table: "order_adjustments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TimeBasisSnapshot",
                table: "order_adjustments",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TimeZoneIdSnapshot",
                table: "order_adjustments",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "WeekdayMaskSnapshot",
                table: "order_adjustments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ApplicationMode",
                table: "order_adjustment_presets",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "CanStack",
                table: "order_adjustment_presets",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "EndMinute",
                table: "order_adjustment_presets",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Priority",
                table: "order_adjustment_presets",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "StartMinute",
                table: "order_adjustment_presets",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TimeBasis",
                table: "order_adjustment_presets",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "WeekdayMask",
                table: "order_adjustment_presets",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "order_adjustment_preset_categories",
                columns: table => new
                {
                    PresetId = table.Column<Guid>(type: "uuid", nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_adjustment_preset_categories", x => new { x.PresetId, x.CategoryId });
                    table.ForeignKey(
                        name: "FK_order_adjustment_preset_categories_categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_order_adjustment_preset_categories_order_adjustment_presets~",
                        column: x => x.PresetId,
                        principalTable: "order_adjustment_presets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "order_adjustment_preset_products",
                columns: table => new
                {
                    PresetId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_adjustment_preset_products", x => new { x.PresetId, x.ProductId });
                    table.ForeignKey(
                        name: "FK_order_adjustment_preset_products_order_adjustment_presets_P~",
                        column: x => x.PresetId,
                        principalTable: "order_adjustment_presets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_order_adjustment_preset_products_products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_order_adjustment_preset_categories_CategoryId",
                table: "order_adjustment_preset_categories",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_order_adjustment_preset_products_ProductId",
                table: "order_adjustment_preset_products",
                column: "ProductId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "order_adjustment_preset_categories");

            migrationBuilder.DropTable(
                name: "order_adjustment_preset_products");

            migrationBuilder.DropColumn(
                name: "CategoryIdSnapshot",
                table: "order_items");

            migrationBuilder.DropColumn(
                name: "ApplicationModeSnapshot",
                table: "order_adjustments");

            migrationBuilder.DropColumn(
                name: "CanStackSnapshot",
                table: "order_adjustments");

            migrationBuilder.DropColumn(
                name: "CategoryIdsSnapshot",
                table: "order_adjustments");

            migrationBuilder.DropColumn(
                name: "EndMinuteSnapshot",
                table: "order_adjustments");

            migrationBuilder.DropColumn(
                name: "PrioritySnapshot",
                table: "order_adjustments");

            migrationBuilder.DropColumn(
                name: "ProductIdsSnapshot",
                table: "order_adjustments");

            migrationBuilder.DropColumn(
                name: "StartMinuteSnapshot",
                table: "order_adjustments");

            migrationBuilder.DropColumn(
                name: "TimeBasisSnapshot",
                table: "order_adjustments");

            migrationBuilder.DropColumn(
                name: "TimeZoneIdSnapshot",
                table: "order_adjustments");

            migrationBuilder.DropColumn(
                name: "WeekdayMaskSnapshot",
                table: "order_adjustments");

            migrationBuilder.DropColumn(
                name: "ApplicationMode",
                table: "order_adjustment_presets");

            migrationBuilder.DropColumn(
                name: "CanStack",
                table: "order_adjustment_presets");

            migrationBuilder.DropColumn(
                name: "EndMinute",
                table: "order_adjustment_presets");

            migrationBuilder.DropColumn(
                name: "Priority",
                table: "order_adjustment_presets");

            migrationBuilder.DropColumn(
                name: "StartMinute",
                table: "order_adjustment_presets");

            migrationBuilder.DropColumn(
                name: "TimeBasis",
                table: "order_adjustment_presets");

            migrationBuilder.DropColumn(
                name: "WeekdayMask",
                table: "order_adjustment_presets");
        }
    }
}
