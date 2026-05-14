using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreateKitchen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FK_FoodStatusId",
                table: "transactions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FoodStatusId",
                table: "transactions",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_transactions_FoodStatusId",
                table: "transactions",
                column: "FoodStatusId");

            migrationBuilder.AddForeignKey(
                name: "FK_transactions_Status_FoodStatusId",
                table: "transactions",
                column: "FoodStatusId",
                principalTable: "Status",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_transactions_Status_FoodStatusId",
                table: "transactions");

            migrationBuilder.DropIndex(
                name: "IX_transactions_FoodStatusId",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "FK_FoodStatusId",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "FoodStatusId",
                table: "transactions");
        }
    }
}
