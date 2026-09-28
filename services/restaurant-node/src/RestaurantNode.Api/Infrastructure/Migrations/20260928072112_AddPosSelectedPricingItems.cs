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
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TargetMode",
                table: "order_adjustment_presets",
                type: "text",
                nullable: false,
                defaultValue: "");
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
