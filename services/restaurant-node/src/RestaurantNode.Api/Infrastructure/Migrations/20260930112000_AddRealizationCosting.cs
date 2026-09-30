using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RestaurantNode.Api.Infrastructure;

#nullable disable

namespace RestaurantNode.Api.Infrastructure.Migrations
{
    [DbContext(typeof(RestaurantDbContext))]
    [Migration("20260930112000_AddRealizationCosting")]
    public partial class AddRealizationCosting : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllowNegativeRealization",
                table: "restaurants",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "InventoryCostMethod",
                table: "restaurants",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "WeightedAverage");

            migrationBuilder.AddColumn<string>(
                name: "CostMethodSnapshot",
                table: "stock_documents",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PostingError",
                table: "stock_documents",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReferenceId",
                table: "stock_documents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReferenceType",
                table: "stock_documents",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_stock_documents_RestaurantId_ReferenceType_ReferenceId",
                table: "stock_documents",
                columns: new[] { "RestaurantId", "ReferenceType", "ReferenceId" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_stock_documents_RestaurantId_ReferenceType_ReferenceId",
                table: "stock_documents");

            migrationBuilder.DropColumn(name: "AllowNegativeRealization", table: "restaurants");
            migrationBuilder.DropColumn(name: "InventoryCostMethod", table: "restaurants");
            migrationBuilder.DropColumn(name: "CostMethodSnapshot", table: "stock_documents");
            migrationBuilder.DropColumn(name: "PostingError", table: "stock_documents");
            migrationBuilder.DropColumn(name: "ReferenceId", table: "stock_documents");
            migrationBuilder.DropColumn(name: "ReferenceType", table: "stock_documents");
        }
    }
}
