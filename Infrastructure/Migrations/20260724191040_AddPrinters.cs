using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPrinters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Printers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Station = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ConnectionType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Address = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CopyCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    ModifiedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Printers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Printers_Name",
                table: "Printers",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Printers_Station_IsEnabled",
                table: "Printers",
                columns: new[] { "Station", "IsEnabled" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Printers");
        }
    }
}
