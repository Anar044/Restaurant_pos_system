using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RestaurantNode.Api.Infrastructure;

#nullable disable

namespace RestaurantNode.Api.Infrastructure.Migrations
{
    [DbContext(typeof(RestaurantDbContext))]
    [Migration("20260929120000_AddGroupDefaultPrecheckPrinter")]
    public partial class AddGroupDefaultPrecheckPrinter : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DefaultPrecheckPrinterId",
                table: "restaurant_groups",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_restaurant_groups_DefaultPrecheckPrinterId",
                table: "restaurant_groups",
                column: "DefaultPrecheckPrinterId");

            migrationBuilder.AddForeignKey(
                name: "FK_restaurant_groups_printers_DefaultPrecheckPrinterId",
                table: "restaurant_groups",
                column: "DefaultPrecheckPrinterId",
                principalTable: "printers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_restaurant_groups_printers_DefaultPrecheckPrinterId",
                table: "restaurant_groups");

            migrationBuilder.DropIndex(
                name: "IX_restaurant_groups_DefaultPrecheckPrinterId",
                table: "restaurant_groups");

            migrationBuilder.DropColumn(
                name: "DefaultPrecheckPrinterId",
                table: "restaurant_groups");
        }
    }
}
