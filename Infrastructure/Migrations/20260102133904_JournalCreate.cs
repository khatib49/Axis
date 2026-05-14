using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class JournalCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AccountId",
                table: "ExpenseCategory",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseCategory_AccountId",
                table: "ExpenseCategory",
                column: "AccountId");

            migrationBuilder.AddForeignKey(
                name: "FK_ExpenseCategory_accounts_AccountId",
                table: "ExpenseCategory",
                column: "AccountId",
                principalTable: "accounts",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ExpenseCategory_accounts_AccountId",
                table: "ExpenseCategory");

            migrationBuilder.DropIndex(
                name: "IX_ExpenseCategory_AccountId",
                table: "ExpenseCategory");

            migrationBuilder.DropColumn(
                name: "AccountId",
                table: "ExpenseCategory");
        }
    }
}
