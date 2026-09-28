using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantNode.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddShiftCashReconciliation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CashDifference",
                table: "shifts",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClosingNote",
                table: "shifts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ExpectedCashAtClose",
                table: "shifts",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_shifts_RestaurantId_DeviceId_Status_OpenedAt",
                table: "shifts",
                columns: new[] { "RestaurantId", "DeviceId", "Status", "OpenedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_shifts_RestaurantId_DeviceId_Status_OpenedAt",
                table: "shifts");

            migrationBuilder.DropColumn(
                name: "CashDifference",
                table: "shifts");

            migrationBuilder.DropColumn(
                name: "ClosingNote",
                table: "shifts");

            migrationBuilder.DropColumn(
                name: "ExpectedCashAtClose",
                table: "shifts");
        }
    }
}
