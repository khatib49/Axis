using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LinkRoomAndGameTransactions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_transactions_AspNetUsers_UserId",
                table: "transactions");

            migrationBuilder.DropForeignKey(
                name: "FK_transactions_Cards_CardId",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "Price",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "Reference",
                table: "transactions");

            migrationBuilder.RenameColumn(
                name: "UserId",
                table: "transactions",
                newName: "RoomId");

            migrationBuilder.RenameColumn(
                name: "Type",
                table: "transactions",
                newName: "CreatedBy");

            migrationBuilder.RenameIndex(
                name: "IX_transactions_UserId",
                table: "transactions",
                newName: "IX_transactions_RoomId");

            migrationBuilder.AlterColumn<Guid>(
                name: "CardId",
                table: "transactions",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedOn",
                table: "transactions",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "NOW()");

            migrationBuilder.AddColumn<Guid>(
                name: "GameId",
                table: "transactions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "GameSettingId",
                table: "transactions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "GameTypeId",
                table: "transactions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<int>(
                name: "Hours",
                table: "transactions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "ModifiedOn",
                table: "transactions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalPrice",
                table: "transactions",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateIndex(
                name: "IX_transactions_GameId",
                table: "transactions",
                column: "GameId");

            migrationBuilder.CreateIndex(
                name: "IX_transactions_GameSettingId",
                table: "transactions",
                column: "GameSettingId");

            migrationBuilder.CreateIndex(
                name: "IX_transactions_GameTypeId",
                table: "transactions",
                column: "GameTypeId");

            migrationBuilder.AddForeignKey(
                name: "FK_transactions_Cards_CardId",
                table: "transactions",
                column: "CardId",
                principalTable: "Cards",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_transactions_Categories_GameTypeId",
                table: "transactions",
                column: "GameTypeId",
                principalTable: "Categories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_transactions_Games_GameId",
                table: "transactions",
                column: "GameId",
                principalTable: "Games",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_transactions_Rooms_RoomId",
                table: "transactions",
                column: "RoomId",
                principalTable: "Rooms",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_transactions_Settings_GameSettingId",
                table: "transactions",
                column: "GameSettingId",
                principalTable: "Settings",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_transactions_Cards_CardId",
                table: "transactions");

            migrationBuilder.DropForeignKey(
                name: "FK_transactions_Categories_GameTypeId",
                table: "transactions");

            migrationBuilder.DropForeignKey(
                name: "FK_transactions_Games_GameId",
                table: "transactions");

            migrationBuilder.DropForeignKey(
                name: "FK_transactions_Rooms_RoomId",
                table: "transactions");

            migrationBuilder.DropForeignKey(
                name: "FK_transactions_Settings_GameSettingId",
                table: "transactions");

            migrationBuilder.DropIndex(
                name: "IX_transactions_GameId",
                table: "transactions");

            migrationBuilder.DropIndex(
                name: "IX_transactions_GameSettingId",
                table: "transactions");

            migrationBuilder.DropIndex(
                name: "IX_transactions_GameTypeId",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "CreatedOn",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "GameId",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "GameSettingId",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "GameTypeId",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "Hours",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "ModifiedOn",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "TotalPrice",
                table: "transactions");

            migrationBuilder.RenameColumn(
                name: "RoomId",
                table: "transactions",
                newName: "UserId");

            migrationBuilder.RenameColumn(
                name: "CreatedBy",
                table: "transactions",
                newName: "Type");

            migrationBuilder.RenameIndex(
                name: "IX_transactions_RoomId",
                table: "transactions",
                newName: "IX_transactions_UserId");

            migrationBuilder.AlterColumn<Guid>(
                name: "CardId",
                table: "transactions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Price",
                table: "transactions",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "Reference",
                table: "transactions",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddForeignKey(
                name: "FK_transactions_AspNetUsers_UserId",
                table: "transactions",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_transactions_Cards_CardId",
                table: "transactions",
                column: "CardId",
                principalTable: "Cards",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
