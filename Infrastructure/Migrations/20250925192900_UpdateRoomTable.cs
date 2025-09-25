using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UpdateRoomTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Rooms_AspNetUsers_AssignedUserId",
                table: "Rooms");

            migrationBuilder.DropForeignKey(
                name: "FK_Rooms_Games_GameId",
                table: "Rooms");

            migrationBuilder.DropIndex(
                name: "IX_Rooms_GameId",
                table: "Rooms");

            migrationBuilder.DropColumn(
                name: "CurrentSessionStartTime",
                table: "Rooms");

            migrationBuilder.DropColumn(
                name: "GameId",
                table: "Rooms");

            migrationBuilder.DropColumn(
                name: "IsAvailable",
                table: "Rooms");

            migrationBuilder.RenameColumn(
                name: "AssignedUserId",
                table: "Rooms",
                newName: "CategoryId");

            migrationBuilder.RenameIndex(
                name: "IX_Rooms_AssignedUserId",
                table: "Rooms",
                newName: "IX_Rooms_CategoryId");

            migrationBuilder.AddColumn<int>(
                name: "Sets",
                table: "Rooms",
                type: "integer",
                nullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Rooms_Categories_CategoryId",
                table: "Rooms",
                column: "CategoryId",
                principalTable: "Categories",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Rooms_Categories_CategoryId",
                table: "Rooms");

            migrationBuilder.DropColumn(
                name: "Sets",
                table: "Rooms");

            migrationBuilder.RenameColumn(
                name: "CategoryId",
                table: "Rooms",
                newName: "AssignedUserId");

            migrationBuilder.RenameIndex(
                name: "IX_Rooms_CategoryId",
                table: "Rooms",
                newName: "IX_Rooms_AssignedUserId");

            migrationBuilder.AddColumn<DateTime>(
                name: "CurrentSessionStartTime",
                table: "Rooms",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "GameId",
                table: "Rooms",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<bool>(
                name: "IsAvailable",
                table: "Rooms",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_Rooms_GameId",
                table: "Rooms",
                column: "GameId");

            migrationBuilder.AddForeignKey(
                name: "FK_Rooms_AspNetUsers_AssignedUserId",
                table: "Rooms",
                column: "AssignedUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Rooms_Games_GameId",
                table: "Rooms",
                column: "GameId",
                principalTable: "Games",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
