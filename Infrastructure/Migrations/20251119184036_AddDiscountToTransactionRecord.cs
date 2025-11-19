using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDiscountToTransactionRecord : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DiscountId",
                table: "transactions",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_transactions_DiscountId",
                table: "transactions",
                column: "DiscountId");

            migrationBuilder.AddForeignKey(
                name: "FK_transactions_Discounts_DiscountId",
                table: "transactions",
                column: "DiscountId",
                principalTable: "Discounts",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_transactions_Discounts_DiscountId",
                table: "transactions");

            migrationBuilder.DropIndex(
                name: "IX_transactions_DiscountId",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "DiscountId",
                table: "transactions");
        }
    }
}
