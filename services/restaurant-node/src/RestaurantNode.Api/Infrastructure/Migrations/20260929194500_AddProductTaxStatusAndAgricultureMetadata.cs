using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RestaurantNode.Api.Infrastructure;

#nullable disable

namespace RestaurantNode.Api.Infrastructure.Migrations
{
    [DbContext(typeof(RestaurantDbContext))]
    [Migration("20260929194500_AddProductTaxStatusAndAgricultureMetadata")]
    public partial class AddProductTaxStatusAndAgricultureMetadata : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TaxStatus",
                table: "products",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "STANDARD");

            migrationBuilder.AddColumn<bool>(
                name: "OwnAgricultureSameTaxpayer",
                table: "products",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "OwnAgricultureCriteriaMet",
                table: "products",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "OwnAgricultureUnprocessed",
                table: "products",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProductionUnit",
                table: "products",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OriginDocument",
                table: "products",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ProductionOrHarvestDate",
                table: "products",
                type: "date",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "TaxStatus", table: "products");
            migrationBuilder.DropColumn(name: "OwnAgricultureSameTaxpayer", table: "products");
            migrationBuilder.DropColumn(name: "OwnAgricultureCriteriaMet", table: "products");
            migrationBuilder.DropColumn(name: "OwnAgricultureUnprocessed", table: "products");
            migrationBuilder.DropColumn(name: "ProductionUnit", table: "products");
            migrationBuilder.DropColumn(name: "OriginDocument", table: "products");
            migrationBuilder.DropColumn(name: "ProductionOrHarvestDate", table: "products");
        }
    }
}
