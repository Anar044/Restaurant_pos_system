using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantNode.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEquipmentAndPrinters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PrinterId",
                table: "kitchen_stations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReceiptPrinterId",
                table: "devices",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "printers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    ConnectionType = table.Column<string>(type: "text", nullable: false),
                    Address = table.Column<string>(type: "text", nullable: false),
                    Port = table.Column<int>(type: "integer", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_printers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_printers_restaurants_RestaurantId",
                        column: x => x.RestaurantId,
                        principalTable: "restaurants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_kitchen_stations_PrinterId",
                table: "kitchen_stations",
                column: "PrinterId");

            migrationBuilder.CreateIndex(
                name: "IX_devices_ReceiptPrinterId",
                table: "devices",
                column: "ReceiptPrinterId");

            migrationBuilder.CreateIndex(
                name: "IX_printers_RestaurantId_Name",
                table: "printers",
                columns: new[] { "RestaurantId", "Name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_devices_printers_ReceiptPrinterId",
                table: "devices",
                column: "ReceiptPrinterId",
                principalTable: "printers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_kitchen_stations_printers_PrinterId",
                table: "kitchen_stations",
                column: "PrinterId",
                principalTable: "printers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_devices_printers_ReceiptPrinterId",
                table: "devices");

            migrationBuilder.DropForeignKey(
                name: "FK_kitchen_stations_printers_PrinterId",
                table: "kitchen_stations");

            migrationBuilder.DropTable(
                name: "printers");

            migrationBuilder.DropIndex(
                name: "IX_kitchen_stations_PrinterId",
                table: "kitchen_stations");

            migrationBuilder.DropIndex(
                name: "IX_devices_ReceiptPrinterId",
                table: "devices");

            migrationBuilder.DropColumn(
                name: "PrinterId",
                table: "kitchen_stations");

            migrationBuilder.DropColumn(
                name: "ReceiptPrinterId",
                table: "devices");
        }
    }
}
