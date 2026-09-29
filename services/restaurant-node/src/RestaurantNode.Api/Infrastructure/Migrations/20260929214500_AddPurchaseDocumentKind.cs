using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RestaurantNode.Api.Infrastructure;

#nullable disable

namespace RestaurantNode.Api.Infrastructure.Migrations
{
    [DbContext(typeof(RestaurantDbContext))]
    [Migration("20260929214500_AddPurchaseDocumentKind")]
    public partial class AddPurchaseDocumentKind : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PurchaseDocumentKind",
                table: "stock_documents",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "SUPPLIER_INVOICE");

            migrationBuilder.AddColumn<string>(
                name: "PurchaseReferenceNumber",
                table: "stock_documents",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "PurchaseDocumentKind", table: "stock_documents");
            migrationBuilder.DropColumn(name: "PurchaseReferenceNumber", table: "stock_documents");
        }
    }
}
