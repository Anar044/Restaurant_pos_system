using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RestaurantNode.Api.Infrastructure;

#nullable disable

namespace RestaurantNode.Api.Infrastructure.Migrations
{
    [DbContext(typeof(RestaurantDbContext))]
    [Migration("20260929140000_AddWarehouseDocumentsAndCosting")]
    public partial class AddWarehouseDocumentsAndCosting : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CostDelta",
                table: "stock_movements",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "UnitCost",
                table: "stock_movements",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "suppliers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false),
                    TaxId = table.Column<string>(type: "text", nullable: true),
                    Phone = table.Column<string>(type: "text", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_suppliers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "stock_documents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Number = table.Column<string>(type: "text", nullable: false),
                    DocumentDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: true),
                    FromWarehouseId = table.Column<Guid>(type: "uuid", nullable: true),
                    ToWarehouseId = table.Column<Guid>(type: "uuid", nullable: true),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: true),
                    TotalAmount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Comment = table.Column<string>(type: "text", nullable: true),
                    CreatedByEmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    PostedByEmployeeId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PostedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_documents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_stock_documents_suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_stock_documents_warehouses_FromWarehouseId",
                        column: x => x.FromWarehouseId,
                        principalTable: "warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_stock_documents_warehouses_ToWarehouseId",
                        column: x => x.ToWarehouseId,
                        principalTable: "warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_stock_documents_warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_document_lines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_document_lines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_stock_document_lines_products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_stock_document_lines_stock_documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "stock_documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_suppliers_RestaurantId_Name",
                table: "suppliers",
                columns: new[] { "RestaurantId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stock_documents_FromWarehouseId",
                table: "stock_documents",
                column: "FromWarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_documents_SupplierId",
                table: "stock_documents",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_documents_ToWarehouseId",
                table: "stock_documents",
                column: "ToWarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_documents_WarehouseId",
                table: "stock_documents",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_documents_RestaurantId_Number",
                table: "stock_documents",
                columns: new[] { "RestaurantId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stock_documents_RestaurantId_Type_Status_DocumentDate",
                table: "stock_documents",
                columns: new[] { "RestaurantId", "Type", "Status", "DocumentDate" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_document_lines_ProductId",
                table: "stock_document_lines",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_document_lines_DocumentId_ProductId",
                table: "stock_document_lines",
                columns: new[] { "DocumentId", "ProductId" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "stock_document_lines");
            migrationBuilder.DropTable(name: "stock_documents");
            migrationBuilder.DropTable(name: "suppliers");

            migrationBuilder.DropColumn(name: "CostDelta", table: "stock_movements");
            migrationBuilder.DropColumn(name: "UnitCost", table: "stock_movements");
        }
    }
}
