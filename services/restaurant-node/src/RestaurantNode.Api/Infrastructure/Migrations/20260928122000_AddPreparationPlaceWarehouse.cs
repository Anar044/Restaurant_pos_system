using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RestaurantNode.Api.Infrastructure;

#nullable disable

namespace RestaurantNode.Api.Infrastructure.Migrations
{
    [DbContext(typeof(RestaurantDbContext))]
    [Migration("20260928122000_AddPreparationPlaceWarehouse")]
    public partial class AddPreparationPlaceWarehouse : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "WarehouseId",
                table: "kitchen_stations",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_kitchen_stations_WarehouseId",
                table: "kitchen_stations",
                column: "WarehouseId");

            migrationBuilder.AddForeignKey(
                name: "FK_kitchen_stations_warehouses_WarehouseId",
                table: "kitchen_stations",
                column: "WarehouseId",
                principalTable: "warehouses",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_kitchen_stations_warehouses_WarehouseId",
                table: "kitchen_stations");

            migrationBuilder.DropIndex(
                name: "IX_kitchen_stations_WarehouseId",
                table: "kitchen_stations");

            migrationBuilder.DropColumn(
                name: "WarehouseId",
                table: "kitchen_stations");
        }
    }
}
