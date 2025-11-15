using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ChangeDiscountAmountToInt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Amount",
                table: "Discounts");

            migrationBuilder.AddColumn<int>(
                name: "Percentage",
                table: "Discounts",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Percentage",
                table: "Discounts");

            migrationBuilder.AddColumn<decimal>(
                name: "Amount",
                table: "Discounts",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);
        }
    }
}
