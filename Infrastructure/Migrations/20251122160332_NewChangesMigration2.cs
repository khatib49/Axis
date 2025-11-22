using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class NewChangesMigration2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "StatusId",
                table: "Sets",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sets_StatusId",
                table: "Sets",
                column: "StatusId");

            migrationBuilder.AddForeignKey(
                name: "FK_Sets_Status_StatusId",
                table: "Sets",
                column: "StatusId",
                principalTable: "Status",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Sets_Status_StatusId",
                table: "Sets");

            migrationBuilder.DropIndex(
                name: "IX_Sets_StatusId",
                table: "Sets");

            migrationBuilder.DropColumn(
                name: "StatusId",
                table: "Sets");
        }
    }
}
