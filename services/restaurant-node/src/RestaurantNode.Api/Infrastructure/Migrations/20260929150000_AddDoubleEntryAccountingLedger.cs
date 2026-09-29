using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RestaurantNode.Api.Infrastructure;

#nullable disable

namespace RestaurantNode.Api.Infrastructure.Migrations
{
    [DbContext(typeof(RestaurantDbContext))]
    [Migration("20260929150000_AddDoubleEntryAccountingLedger")]
    public partial class AddDoubleEntryAccountingLedger : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ledger_accounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false),
                    SystemKey = table.Column<string>(type: "text", nullable: true),
                    IsSystem = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ledger_accounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ledger_entries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReferenceType = table.Column<string>(type: "text", nullable: false),
                    ReferenceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ledger_entries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ledger_lines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EntryId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Debit = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Credit = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: true),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: true),
                    MoneyAccountId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ledger_lines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ledger_lines_ledger_accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "ledger_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ledger_lines_ledger_entries_EntryId",
                        column: x => x.EntryId,
                        principalTable: "ledger_entries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ledger_accounts_RestaurantId_Code",
                table: "ledger_accounts",
                columns: new[] { "RestaurantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ledger_accounts_RestaurantId_SystemKey",
                table: "ledger_accounts",
                columns: new[] { "RestaurantId", "SystemKey" },
                unique: true,
                filter: "\"SystemKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ledger_entries_RestaurantId_OccurredAt",
                table: "ledger_entries",
                columns: new[] { "RestaurantId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ledger_entries_RestaurantId_ReferenceType_ReferenceId",
                table: "ledger_entries",
                columns: new[] { "RestaurantId", "ReferenceType", "ReferenceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ledger_lines_AccountId",
                table: "ledger_lines",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_ledger_lines_EntryId",
                table: "ledger_lines",
                column: "EntryId");

            migrationBuilder.CreateIndex(
                name: "IX_ledger_lines_RestaurantId_MoneyAccountId",
                table: "ledger_lines",
                columns: new[] { "RestaurantId", "MoneyAccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_ledger_lines_RestaurantId_SupplierId",
                table: "ledger_lines",
                columns: new[] { "RestaurantId", "SupplierId" });

            migrationBuilder.CreateIndex(
                name: "IX_ledger_lines_RestaurantId_WarehouseId",
                table: "ledger_lines",
                columns: new[] { "RestaurantId", "WarehouseId" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ledger_lines");
            migrationBuilder.DropTable(name: "ledger_accounts");
            migrationBuilder.DropTable(name: "ledger_entries");
        }
    }
}
