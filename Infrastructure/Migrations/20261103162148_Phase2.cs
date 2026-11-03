using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Floor_OrderCourses_TransactionId_ItemId",
                table: "Floor_OrderCourses");

            migrationBuilder.AddColumn<int>(
                name: "BranchId",
                table: "transactions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "Promo_HappyHourApplications",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "ConfirmationSentOn",
                table: "Floor_Reservations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReminderSentOn",
                table: "Floor_Reservations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Quantity",
                table: "Floor_OrderCourses",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "SeatNumber",
                table: "Floor_OrderCourses",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Pay_WhishRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TransactionId = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    CollectUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ReturnUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    PaidOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastCheckedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RawCallback = table.Column<string>(type: "text", nullable: true),
                    BranchId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Pay_WhishRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Pay_WhishRequests_transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Promo_ComboApplications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ComboId = table.Column<int>(type: "integer", nullable: false),
                    TransactionId = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    On = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    By = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    BranchId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Promo_ComboApplications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Promo_ComboApplications_Promo_Combos_ComboId",
                        column: x => x.ComboId,
                        principalTable: "Promo_Combos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Promo_ComboApplications_transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Till_DisplayStates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RegisterName = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Token = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TransactionId = table.Column<int>(type: "integer", nullable: true),
                    CartJson = table.Column<string>(type: "text", nullable: false),
                    TotalUsd = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    RateLbp = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    TotalLbp = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    WhishRequestId = table.Column<int>(type: "integer", nullable: true),
                    UpdatedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    BranchId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Till_DisplayStates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Till_DisplayStates_Pay_WhishRequests_WhishRequestId",
                        column: x => x.WhishRequestId,
                        principalTable: "Pay_WhishRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Till_DisplayStates_transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_transactions_BranchId",
                table: "transactions",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_Floor_OrderCourses_TransactionId_ItemId_SeatNumber",
                table: "Floor_OrderCourses",
                columns: new[] { "TransactionId", "ItemId", "SeatNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Pay_WhishRequests_BranchId",
                table: "Pay_WhishRequests",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_Pay_WhishRequests_ExternalId",
                table: "Pay_WhishRequests",
                column: "ExternalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Pay_WhishRequests_Status",
                table: "Pay_WhishRequests",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Pay_WhishRequests_TransactionId",
                table: "Pay_WhishRequests",
                column: "TransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_Promo_ComboApplications_ComboId_TransactionId",
                table: "Promo_ComboApplications",
                columns: new[] { "ComboId", "TransactionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Promo_ComboApplications_TransactionId",
                table: "Promo_ComboApplications",
                column: "TransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_Till_DisplayStates_RegisterName",
                table: "Till_DisplayStates",
                column: "RegisterName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Till_DisplayStates_Token",
                table: "Till_DisplayStates",
                column: "Token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Till_DisplayStates_TransactionId",
                table: "Till_DisplayStates",
                column: "TransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_Till_DisplayStates_WhishRequestId",
                table: "Till_DisplayStates",
                column: "WhishRequestId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Promo_ComboApplications");

            migrationBuilder.DropTable(
                name: "Till_DisplayStates");

            migrationBuilder.DropTable(
                name: "Pay_WhishRequests");

            migrationBuilder.DropIndex(
                name: "IX_transactions_BranchId",
                table: "transactions");

            migrationBuilder.DropIndex(
                name: "IX_Floor_OrderCourses_TransactionId_ItemId_SeatNumber",
                table: "Floor_OrderCourses");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Promo_HappyHourApplications");

            migrationBuilder.DropColumn(
                name: "ConfirmationSentOn",
                table: "Floor_Reservations");

            migrationBuilder.DropColumn(
                name: "ReminderSentOn",
                table: "Floor_Reservations");

            migrationBuilder.DropColumn(
                name: "Quantity",
                table: "Floor_OrderCourses");

            migrationBuilder.DropColumn(
                name: "SeatNumber",
                table: "Floor_OrderCourses");

            migrationBuilder.CreateIndex(
                name: "IX_Floor_OrderCourses_TransactionId_ItemId",
                table: "Floor_OrderCourses",
                columns: new[] { "TransactionId", "ItemId" },
                unique: true);
        }
    }
}
