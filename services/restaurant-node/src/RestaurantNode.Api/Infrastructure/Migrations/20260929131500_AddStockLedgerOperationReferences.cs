using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RestaurantNode.Api.Infrastructure;

#nullable disable

namespace RestaurantNode.Api.Infrastructure.Migrations
{
    [DbContext(typeof(RestaurantDbContext))]
    [Migration("20260929131500_AddStockLedgerOperationReferences")]
    public partial class AddStockLedgerOperationReferences : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "OperationId",
                table: "stock_movements",
                type: "uuid",
                nullable: false,
                defaultValue: Guid.Empty);

            migrationBuilder.AddColumn<Guid>(
                name: "ReferenceId",
                table: "stock_movements",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReferenceType",
                table: "stock_movements",
                type: "text",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE stock_movements
                SET "OperationId" = "Id"
                WHERE "OperationId" = '00000000-0000-0000-0000-000000000000';
                """);

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_RestaurantId_OperationId",
                table: "stock_movements",
                columns: new[] { "RestaurantId", "OperationId" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_RestaurantId_ReferenceType_ReferenceId",
                table: "stock_movements",
                columns: new[] { "RestaurantId", "ReferenceType", "ReferenceId" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_stock_movements_RestaurantId_OperationId",
                table: "stock_movements");

            migrationBuilder.DropIndex(
                name: "IX_stock_movements_RestaurantId_ReferenceType_ReferenceId",
                table: "stock_movements");

            migrationBuilder.DropColumn(
                name: "OperationId",
                table: "stock_movements");

            migrationBuilder.DropColumn(
                name: "ReferenceId",
                table: "stock_movements");

            migrationBuilder.DropColumn(
                name: "ReferenceType",
                table: "stock_movements");
        }
    }
}
