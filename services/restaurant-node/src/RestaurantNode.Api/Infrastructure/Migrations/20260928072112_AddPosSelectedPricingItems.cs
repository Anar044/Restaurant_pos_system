using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantNode.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPosSelectedPricingItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid[]>(
                name: "OrderItemIdsSnapshot",
                table: "order_adjustments",
                type: "uuid[]",
                nullable: false,
                defaultValue: new Guid[0]);

            migrationBuilder.AddColumn<string>(
                name: "TargetModeSnapshot",
                table: "order_adjustments",
                type: "text",
                nullable: false,
                defaultValue: "AllItems");

            migrationBuilder.AddColumn<string>(
                name: "TargetMode",
                table: "order_adjustment_presets",
                type: "text",
                nullable: false,
                defaultValue: "AllItems");

            migrationBuilder.Sql(
                """
                UPDATE order_adjustment_presets AS preset
                SET "TargetMode" = 'PresetSelection'
                WHERE EXISTS (
                    SELECT 1
                    FROM order_adjustment_preset_products AS product_link
                    WHERE product_link."PresetId" = preset."Id"
                )
                OR EXISTS (
                    SELECT 1
                    FROM order_adjustment_preset_categories AS category_link
                    WHERE category_link."PresetId" = preset."Id"
                );
                """);

            migrationBuilder.Sql(
                """
                UPDATE order_adjustments
                SET "TargetModeSnapshot" = 'PresetSelection'
                WHERE cardinality("ProductIdsSnapshot") > 0
                   OR cardinality("CategoryIdsSnapshot") > 0;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OrderItemIdsSnapshot",
                table: "order_adjustments");

            migrationBuilder.DropColumn(
                name: "TargetModeSnapshot",
                table: "order_adjustments");

            migrationBuilder.DropColumn(
                name: "TargetMode",
                table: "order_adjustment_presets");
        }
    }
}
