using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantNode.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddManagedAdjustmentPresets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PresetId",
                table: "order_adjustments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PresetNameSnapshot",
                table: "order_adjustments",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "order_adjustment_presets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false),
                    Mode = table.Column<string>(type: "text", nullable: false),
                    Scope = table.Column<string>(type: "text", nullable: false),
                    Value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_adjustment_presets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "order_adjustment_preset_roles",
                columns: table => new
                {
                    PresetId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_adjustment_preset_roles", x => new { x.PresetId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_order_adjustment_preset_roles_order_adjustment_presets_Pres~",
                        column: x => x.PresetId,
                        principalTable: "order_adjustment_presets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_order_adjustment_preset_roles_roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_order_adjustment_preset_roles_RoleId",
                table: "order_adjustment_preset_roles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_order_adjustment_presets_RestaurantId_Name",
                table: "order_adjustment_presets",
                columns: new[] { "RestaurantId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "order_adjustment_preset_roles");

            migrationBuilder.DropTable(
                name: "order_adjustment_presets");

            migrationBuilder.DropColumn(
                name: "PresetId",
                table: "order_adjustments");

            migrationBuilder.DropColumn(
                name: "PresetNameSnapshot",
                table: "order_adjustments");
        }
    }
}
