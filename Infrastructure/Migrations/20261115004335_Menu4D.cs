using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Menu4D : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Menu4D_Profiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    PartsJson = table.Column<string>(type: "text", nullable: true),
                    PartsPublished = table.Column<bool>(type: "boolean", nullable: false),
                    RealSizeCm = table.Column<decimal>(type: "numeric(6,1)", precision: 6, scale: 1, nullable: true),
                    GenStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    GenProvider = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    GenTaskId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    GenProgress = table.Column<int>(type: "integer", nullable: false),
                    GenError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    GenQueuedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    GenStartedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    GenFinishedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Menu4D_Profiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Menu4D_Profiles_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Menu4D_Profiles_GenStatus",
                table: "Menu4D_Profiles",
                column: "GenStatus");

            migrationBuilder.CreateIndex(
                name: "IX_Menu4D_Profiles_ItemId",
                table: "Menu4D_Profiles",
                column: "ItemId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Menu4D_Profiles");
        }
    }
}
