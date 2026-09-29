using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RestaurantNode.Api.Infrastructure;

#nullable disable

namespace RestaurantNode.Api.Infrastructure.Migrations
{
    [DbContext(typeof(RestaurantDbContext))]
    [Migration("20260929133000_AddMoneyAccountingLedger")]
    public partial class AddMoneyAccountingLedger : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "money_accounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_money_accounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "money_categories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Direction = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_money_categories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "money_transactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Direction = table.Column<string>(type: "text", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Note = table.Column<string>(type: "text", nullable: true),
                    ReferenceType = table.Column<string>(type: "text", nullable: true),
                    ReferenceId = table.Column<Guid>(type: "uuid", nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_money_transactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_money_transactions_money_accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "money_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_money_transactions_money_categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "money_categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_money_accounts_RestaurantId_Name",
                table: "money_accounts",
                columns: new[] { "RestaurantId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_money_categories_RestaurantId_Direction_Name",
                table: "money_categories",
                columns: new[] { "RestaurantId", "Direction", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_money_transactions_AccountId",
                table: "money_transactions",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_money_transactions_CategoryId",
                table: "money_transactions",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_money_transactions_RestaurantId_AccountId_OccurredAt",
                table: "money_transactions",
                columns: new[] { "RestaurantId", "AccountId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_money_transactions_RestaurantId_CategoryId_OccurredAt",
                table: "money_transactions",
                columns: new[] { "RestaurantId", "CategoryId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_money_transactions_RestaurantId_ReferenceType_ReferenceId",
                table: "money_transactions",
                columns: new[] { "RestaurantId", "ReferenceType", "ReferenceId" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "money_transactions");
            migrationBuilder.DropTable(name: "money_accounts");
            migrationBuilder.DropTable(name: "money_categories");
        }
    }
}
