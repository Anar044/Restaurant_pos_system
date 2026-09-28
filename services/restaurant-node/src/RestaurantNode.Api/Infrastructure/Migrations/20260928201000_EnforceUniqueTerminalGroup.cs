using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RestaurantNode.Api.Infrastructure;

#nullable disable

namespace RestaurantNode.Api.Infrastructure.Migrations
{
    [DbContext(typeof(RestaurantDbContext))]
    [Migration("20260928201000_EnforceUniqueTerminalGroup")]
    public partial class EnforceUniqueTerminalGroup : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DELETE FROM restaurant_group_devices a
USING restaurant_group_devices b
WHERE a.""DeviceId"" = b.""DeviceId""
  AND (
      CASE WHEN a.""IsMainCashRegister"" THEN 0 ELSE 1 END,
      a.""GroupId""
  ) > (
      CASE WHEN b.""IsMainCashRegister"" THEN 0 ELSE 1 END,
      b.""GroupId""
  );
");

            migrationBuilder.DropIndex(
                name: "IX_restaurant_group_devices_DeviceId",
                table: "restaurant_group_devices");

            migrationBuilder.CreateIndex(
                name: "IX_restaurant_group_devices_DeviceId",
                table: "restaurant_group_devices",
                column: "DeviceId",
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_restaurant_group_devices_DeviceId",
                table: "restaurant_group_devices");

            migrationBuilder.CreateIndex(
                name: "IX_restaurant_group_devices_DeviceId",
                table: "restaurant_group_devices",
                column: "DeviceId");
        }
    }
}
