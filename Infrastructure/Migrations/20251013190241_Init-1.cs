using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Init1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Sets",
                table: "Rooms");

            migrationBuilder.AddColumn<int>(
                name: "SetId",
                table: "transactions",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Sets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RoomId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Sets_Rooms_RoomId",
                        column: x => x.RoomId,
                        principalTable: "Rooms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_transactions_SetId",
                table: "transactions",
                column: "SetId");

            migrationBuilder.CreateIndex(
                name: "IX_Sets_RoomId_Name",
                table: "Sets",
                columns: new[] { "RoomId", "Name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_transactions_Sets_SetId",
                table: "transactions",
                column: "SetId",
                principalTable: "Sets",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_transactions_Sets_SetId",
                table: "transactions");

            migrationBuilder.DropTable(
                name: "Sets");

            migrationBuilder.DropIndex(
                name: "IX_transactions_SetId",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "SetId",
                table: "transactions");

            migrationBuilder.AddColumn<int>(
                name: "Sets",
                table: "Rooms",
                type: "integer",
                nullable: true);
        }
    }
}
