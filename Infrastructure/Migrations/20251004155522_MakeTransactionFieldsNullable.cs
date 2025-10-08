using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MakeTransactionFieldsNullable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "RoomId",
                table: "transactions",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "uuid");

            migrationBuilder.AlterColumn<int>(
                name: "GameTypeId",
                table: "transactions",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "uuid");

            migrationBuilder.AlterColumn<int>(
                name: "GameSettingId",
                table: "transactions",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "uuid");

            migrationBuilder.AlterColumn<int>(
                name: "GameId",
                table: "transactions",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "uuid");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "RoomId",
                table: "transactions",
                type: "uuid",
                nullable: false,
                defaultValue: 1,
                oldClrType: typeof(int),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "GameTypeId",
                table: "transactions",
                type: "uuid",
                nullable: false,
                defaultValue: 1,
                oldClrType: typeof(int),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "GameSettingId",
                table: "transactions",
                type: "uuid",
                nullable: false,
                defaultValue: 1,
                oldClrType: typeof(int),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "GameId",
                table: "transactions",
                type: "uuid",
                nullable: false,
                defaultValue: 1,
                oldClrType: typeof(int),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
