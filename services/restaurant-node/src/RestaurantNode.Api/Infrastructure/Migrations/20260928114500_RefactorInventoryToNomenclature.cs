using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RestaurantNode.Api.Infrastructure;

#nullable disable

namespace RestaurantNode.Api.Infrastructure.Migrations
{
    [DbContext(typeof(RestaurantDbContext))]
    [Migration("20260928114500_RefactorInventoryToNomenclature")]
    public partial class RefactorInventoryToNomenclature : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_products_categories_CategoryId",
                table: "products");

            migrationBuilder.Sql(@"
ALTER TABLE products
DROP CONSTRAINT IF EXISTS ""FK_products_categories_CategoryId"";
");

            migrationBuilder.AlterColumn<Guid>(
                name: "CategoryId",
                table: "products",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddForeignKey(
                name: "FK_products_categories_CategoryId",
                table: "products",
                column: "CategoryId",
                principalTable: "categories",
                principalColumn: "Id");

            migrationBuilder.AddColumn<bool>(
                name: "IsSellable",
                table: "products",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MinStock",
                table: "products",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "TrackStock",
                table: "products",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Type",
                table: "products",
                type: "text",
                nullable: false,
                defaultValue: "DISH");

            migrationBuilder.AddColumn<string>(
                name: "Unit",
                table: "products",
                type: "text",
                nullable: false,
                defaultValue: "pcs");

            migrationBuilder.AddColumn<Guid>(
                name: "ProductId",
                table: "stock_movements",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(@"
INSERT INTO products
    (""Id"", ""RestaurantId"", ""CategoryId"", ""KitchenStationId"", ""Name"", ""Sku"",
     ""Type"", ""Unit"", ""MinStock"", ""TrackStock"", ""IsSellable"", ""IsActive"", ""SortOrder"")
SELECT
    s.""Id"", s.""RestaurantId"", NULL, NULL, s.""Name"", s.""Sku"",
    'GOODS', s.""Unit"", s.""MinStock"", TRUE, FALSE, s.""IsActive"", 0
FROM stock_items s
WHERE NOT EXISTS (SELECT 1 FROM products p WHERE p.""Id"" = s.""Id"");
");

            migrationBuilder.Sql(@"
UPDATE stock_movements
SET ""ProductId"" = ""StockItemId""
WHERE ""ProductId"" IS NULL;
");

            migrationBuilder.AlterColumn<Guid>(
                name: "ProductId",
                table: "stock_movements",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.DropForeignKey(
                name: "FK_stock_movements_stock_items_StockItemId",
                table: "stock_movements");

            migrationBuilder.DropIndex(
                name: "IX_stock_movements_StockItemId",
                table: "stock_movements");

            migrationBuilder.DropIndex(
                name: "IX_stock_movements_RestaurantId_WarehouseId_StockItemId_CreatedAt",
                table: "stock_movements");

            migrationBuilder.DropColumn(
                name: "StockItemId",
                table: "stock_movements");

            migrationBuilder.DropTable(
                name: "stock_items");

            migrationBuilder.CreateTable(
                name: "recipe_lines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    IngredientProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recipe_lines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_recipe_lines_products_IngredientProductId",
                        column: x => x.IngredientProductId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_recipe_lines_products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_products_RestaurantId_Sku",
                table: "products",
                columns: new[] { "RestaurantId", "Sku" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_ProductId",
                table: "stock_movements",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_RestaurantId_WarehouseId_ProductId_CreatedAt",
                table: "stock_movements",
                columns: new[] { "RestaurantId", "WarehouseId", "ProductId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_recipe_lines_IngredientProductId",
                table: "recipe_lines",
                column: "IngredientProductId");

            migrationBuilder.CreateIndex(
                name: "IX_recipe_lines_ProductId_IngredientProductId",
                table: "recipe_lines",
                columns: new[] { "ProductId", "IngredientProductId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_stock_movements_products_ProductId",
                table: "stock_movements",
                column: "ProductId",
                principalTable: "products",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_stock_movements_products_ProductId",
                table: "stock_movements");

            migrationBuilder.DropTable(
                name: "recipe_lines");

            migrationBuilder.DropIndex(
                name: "IX_products_RestaurantId_Sku",
                table: "products");

            migrationBuilder.DropIndex(
                name: "IX_stock_movements_ProductId",
                table: "stock_movements");

            migrationBuilder.DropIndex(
                name: "IX_stock_movements_RestaurantId_WarehouseId_ProductId_CreatedAt",
                table: "stock_movements");

            migrationBuilder.CreateTable(
                name: "stock_items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Sku = table.Column<string>(type: "text", nullable: true),
                    Unit = table.Column<string>(type: "text", nullable: false),
                    MinStock = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_items", x => x.Id);
                });

            migrationBuilder.AddColumn<Guid>(
                name: "StockItemId",
                table: "stock_movements",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(@"
INSERT INTO stock_items
    (""Id"", ""RestaurantId"", ""Name"", ""Sku"", ""Unit"", ""MinStock"", ""IsActive"", ""CreatedAt"")
SELECT
    p.""Id"", p.""RestaurantId"", p.""Name"", p.""Sku"", p.""Unit"", p.""MinStock"", p.""IsActive"", NOW()
FROM products p
WHERE p.""TrackStock"" = TRUE;
");

            migrationBuilder.Sql(@"
UPDATE stock_movements
SET ""StockItemId"" = ""ProductId"";
");

            migrationBuilder.AlterColumn<Guid>(
                name: "StockItemId",
                table: "stock_movements",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.DropColumn(name: "ProductId", table: "stock_movements");
            migrationBuilder.DropColumn(name: "IsSellable", table: "products");
            migrationBuilder.DropColumn(name: "MinStock", table: "products");
            migrationBuilder.DropColumn(name: "TrackStock", table: "products");
            migrationBuilder.DropColumn(name: "Type", table: "products");
            migrationBuilder.DropColumn(name: "Unit", table: "products");

            migrationBuilder.AlterColumn<Guid>(
                name: "CategoryId",
                table: "products",
                type: "uuid",
                nullable: false,
                defaultValue: Guid.Empty,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_products_categories_CategoryId",
                table: "products",
                column: "CategoryId",
                principalTable: "categories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.CreateIndex(
                name: "IX_stock_items_RestaurantId_Name",
                table: "stock_items",
                columns: new[] { "RestaurantId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_items_RestaurantId_Sku",
                table: "stock_items",
                columns: new[] { "RestaurantId", "Sku" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_StockItemId",
                table: "stock_movements",
                column: "StockItemId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_RestaurantId_WarehouseId_StockItemId_CreatedAt",
                table: "stock_movements",
                columns: new[] { "RestaurantId", "WarehouseId", "StockItemId", "CreatedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_stock_movements_stock_items_StockItemId",
                table: "stock_movements",
                column: "StockItemId",
                principalTable: "stock_items",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
