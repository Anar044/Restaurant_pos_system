using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RestaurantNode.Api.Infrastructure;

#nullable disable

namespace RestaurantNode.Api.Infrastructure.Migrations
{
    [DbContext(typeof(RestaurantDbContext))]
    [Migration("20260929212000_AddReceiptVatAccounting")]
    public partial class AddReceiptVatAccounting : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PurchaseSource",
                table: "stock_documents",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "LOCAL");

            migrationBuilder.AddColumn<string>(
                name: "TaxRegimeSnapshot",
                table: "stock_documents",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "UNCONFIGURED");

            migrationBuilder.AddColumn<string>(
                name: "VatPriceMode",
                table: "stock_documents",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "INCLUDED");

            migrationBuilder.AddColumn<string>(
                name: "InputVatCreditStatus",
                table: "stock_documents",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "NOT_APPLICABLE");

            migrationBuilder.AddColumn<string>(
                name: "EInvoiceNumber",
                table: "stock_documents",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "NetAmount",
                table: "stock_documents",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "VatAmount",
                table: "stock_documents",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "InventoryCostAmount",
                table: "stock_documents",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "VatTaxCode",
                table: "stock_document_lines",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "NO_VAT");

            migrationBuilder.AddColumn<decimal>(
                name: "NetAmount",
                table: "stock_document_lines",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "VatAmount",
                table: "stock_document_lines",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "InventoryCostAmount",
                table: "stock_document_lines",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.Sql("""
                UPDATE stock_document_lines
                SET "NetAmount" = "Amount",
                    "InventoryCostAmount" = "Amount"
                WHERE "NetAmount" = 0
                  AND "VatAmount" = 0
                  AND "InventoryCostAmount" = 0;
                """);

            migrationBuilder.Sql("""
                UPDATE stock_documents
                SET "NetAmount" = "TotalAmount",
                    "InventoryCostAmount" = "TotalAmount"
                WHERE "NetAmount" = 0
                  AND "VatAmount" = 0
                  AND "InventoryCostAmount" = 0;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "PurchaseSource", table: "stock_documents");
            migrationBuilder.DropColumn(name: "TaxRegimeSnapshot", table: "stock_documents");
            migrationBuilder.DropColumn(name: "VatPriceMode", table: "stock_documents");
            migrationBuilder.DropColumn(name: "InputVatCreditStatus", table: "stock_documents");
            migrationBuilder.DropColumn(name: "EInvoiceNumber", table: "stock_documents");
            migrationBuilder.DropColumn(name: "NetAmount", table: "stock_documents");
            migrationBuilder.DropColumn(name: "VatAmount", table: "stock_documents");
            migrationBuilder.DropColumn(name: "InventoryCostAmount", table: "stock_documents");

            migrationBuilder.DropColumn(name: "VatTaxCode", table: "stock_document_lines");
            migrationBuilder.DropColumn(name: "NetAmount", table: "stock_document_lines");
            migrationBuilder.DropColumn(name: "VatAmount", table: "stock_document_lines");
            migrationBuilder.DropColumn(name: "InventoryCostAmount", table: "stock_document_lines");
        }
    }
}
