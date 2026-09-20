using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RestaurantNode.Api.Infrastructure;

#nullable disable

namespace RestaurantNode.Api.Infrastructure.Migrations
{
    [DbContext(typeof(RestaurantDbContext))]
    [Migration("20260920222000_AddPaymentCashDetailsAndRefunds")]
    public partial class AddPaymentCashDetailsAndRefunds : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "TenderedAmount",
                table: "payments",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ChangeAmount",
                table: "payments",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "payment_refunds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShiftId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_refunds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_payment_refunds_payments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_payments_RestaurantId_ShiftId_CreatedAt",
                table: "payments",
                columns: new[] { "RestaurantId", "ShiftId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_payment_refunds_PaymentId",
                table: "payment_refunds",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_payment_refunds_RestaurantId_ShiftId_CreatedAt",
                table: "payment_refunds",
                columns: new[] { "RestaurantId", "ShiftId", "CreatedAt" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "payment_refunds");

            migrationBuilder.DropIndex(
                name: "IX_payments_RestaurantId_ShiftId_CreatedAt",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "TenderedAmount",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "ChangeAmount",
                table: "payments");
        }
    }
}
