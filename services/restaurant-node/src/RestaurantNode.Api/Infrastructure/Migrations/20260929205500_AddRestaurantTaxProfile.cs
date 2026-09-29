using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RestaurantNode.Api.Infrastructure;

#nullable disable

namespace RestaurantNode.Api.Infrastructure.Migrations
{
    [DbContext(typeof(RestaurantDbContext))]
    [Migration("20260929205500_AddRestaurantTaxProfile")]
    public partial class AddRestaurantTaxProfile : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TaxRegime",
                table: "restaurants",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "UNCONFIGURED");

            migrationBuilder.AddColumn<string>(
                name: "VatPriceMode",
                table: "restaurants",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "INCLUDED");

            migrationBuilder.AddColumn<bool>(
                name: "IntegratedPosTaxReliefEnabled",
                table: "restaurants",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "TaxRegime", table: "restaurants");
            migrationBuilder.DropColumn(name: "VatPriceMode", table: "restaurants");
            migrationBuilder.DropColumn(name: "IntegratedPosTaxReliefEnabled", table: "restaurants");
        }
    }
}
