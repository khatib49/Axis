using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SettingsMigration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SettingsValues");

            migrationBuilder.DropTable(
                name: "SettingsAttributes");

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "Settings",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedOn",
                table: "Settings",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<decimal>(
                name: "Hours",
                table: "Settings",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "ModifiedBy",
                table: "Settings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ModifiedOn",
                table: "Settings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Price",
                table: "Settings",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "CreatedOn",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "Hours",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "ModifiedBy",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "ModifiedOn",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "Price",
                table: "Settings");

            migrationBuilder.CreateTable(
                name: "SettingsAttributes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SettingsId = table.Column<Guid>(type: "uuid", nullable: false),
                    AttributeValue = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SettingsAttributes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SettingsAttributes_Settings_SettingsId",
                        column: x => x.SettingsId,
                        principalTable: "Settings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SettingsValues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AttributeId = table.Column<Guid>(type: "uuid", nullable: false),
                    SettingsId = table.Column<Guid>(type: "uuid", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SettingsValues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SettingsValues_SettingsAttributes_AttributeId",
                        column: x => x.AttributeId,
                        principalTable: "SettingsAttributes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SettingsValues_Settings_SettingsId",
                        column: x => x.SettingsId,
                        principalTable: "Settings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SettingsAttributes_SettingsId",
                table: "SettingsAttributes",
                column: "SettingsId");

            migrationBuilder.CreateIndex(
                name: "IX_SettingsValues_AttributeId",
                table: "SettingsValues",
                column: "AttributeId");

            migrationBuilder.CreateIndex(
                name: "IX_SettingsValues_SettingsId",
                table: "SettingsValues",
                column: "SettingsId");
        }
    }
}
