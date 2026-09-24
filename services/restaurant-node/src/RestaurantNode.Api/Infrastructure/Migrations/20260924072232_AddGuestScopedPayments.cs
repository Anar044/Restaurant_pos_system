using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantNode.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddGuestScopedPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_payments_OrderId",
                table: "payments");

            migrationBuilder.AddColumn<int>(
                name: "GuestNumber",
                table: "payments",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_payments_OrderId_GuestNumber_CreatedAt",
                table: "payments",
                columns: new[] { "OrderId", "GuestNumber", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_payments_OrderId_GuestNumber_CreatedAt",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "GuestNumber",
                table: "payments");

            migrationBuilder.CreateIndex(
                name: "IX_payments_OrderId",
                table: "payments",
                column: "OrderId");
        }
    }
}
