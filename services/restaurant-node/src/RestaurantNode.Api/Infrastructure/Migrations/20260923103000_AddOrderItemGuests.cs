using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantNode.Api.Infrastructure.Migrations;

public partial class AddOrderItemGuests : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_order_items_OrderId",
            table: "order_items");

        migrationBuilder.AddColumn<int>(
            name: "GuestNumber",
            table: "order_items",
            type: "integer",
            nullable: false,
            defaultValue: 1);

        migrationBuilder.CreateIndex(
            name: "IX_order_items_OrderId_GuestNumber",
            table: "order_items",
            columns: new[] { "OrderId", "GuestNumber" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_order_items_OrderId_GuestNumber",
            table: "order_items");

        migrationBuilder.DropColumn(
            name: "GuestNumber",
            table: "order_items");

        migrationBuilder.CreateIndex(
            name: "IX_order_items_OrderId",
            table: "order_items",
            column: "OrderId");
    }
}
