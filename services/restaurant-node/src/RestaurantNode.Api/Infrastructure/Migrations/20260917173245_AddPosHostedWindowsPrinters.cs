using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantNode.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPosHostedWindowsPrinters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_printers_RestaurantId_Name",
                table: "printers");

            migrationBuilder.AddColumn<Guid>(
                name: "HostDeviceId",
                table: "printers",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_printers_HostDeviceId_Address",
                table: "printers",
                columns: new[] { "HostDeviceId", "Address" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_printers_RestaurantId_Name",
                table: "printers",
                columns: new[] { "RestaurantId", "Name" });

            migrationBuilder.AddForeignKey(
                name: "FK_printers_devices_HostDeviceId",
                table: "printers",
                column: "HostDeviceId",
                principalTable: "devices",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_printers_devices_HostDeviceId",
                table: "printers");

            migrationBuilder.DropIndex(
                name: "IX_printers_HostDeviceId_Address",
                table: "printers");

            migrationBuilder.DropIndex(
                name: "IX_printers_RestaurantId_Name",
                table: "printers");

            migrationBuilder.DropColumn(
                name: "HostDeviceId",
                table: "printers");

            migrationBuilder.CreateIndex(
                name: "IX_printers_RestaurantId_Name",
                table: "printers",
                columns: new[] { "RestaurantId", "Name" },
                unique: true);
        }
    }
}
