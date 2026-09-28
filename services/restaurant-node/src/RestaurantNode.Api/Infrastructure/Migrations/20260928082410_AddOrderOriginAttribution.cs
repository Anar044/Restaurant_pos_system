using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantNode.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderOriginAttribution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "OpenedShiftId",
                table: "orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OriginDeviceId",
                table: "orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_orders_OpenedShiftId_Status",
                table: "orders",
                columns: new[] { "OpenedShiftId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_orders_RestaurantId_OriginDeviceId_CreatedAt",
                table: "orders",
                columns: new[] { "RestaurantId", "OriginDeviceId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_orders_OpenedShiftId_Status",
                table: "orders");

            migrationBuilder.DropIndex(
                name: "IX_orders_RestaurantId_OriginDeviceId_CreatedAt",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "OpenedShiftId",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "OriginDeviceId",
                table: "orders");
        }
    }
}
