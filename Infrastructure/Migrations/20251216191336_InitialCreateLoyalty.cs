using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreateLoyalty : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "loyalty_customers",
                columns: table => new
                {
                    phone_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    total_tickets_current_month = table.Column<int>(type: "integer", nullable: false),
                    pending_balance = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    last_updated = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_loyalty_customers", x => x.phone_number);
                });

            migrationBuilder.CreateTable(
                name: "loyalty_tickets",
                columns: table => new
                {
                    ticket_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    customer_phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    transaction_id = table.Column<int>(type: "integer", nullable: false),
                    tickets_earned = table.Column<int>(type: "integer", nullable: false),
                    earned_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    draw_month = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    is_valid = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_loyalty_tickets", x => x.ticket_id);
                    table.ForeignKey(
                        name: "FK_loyalty_tickets_loyalty_customers_customer_phone",
                        column: x => x.customer_phone,
                        principalTable: "loyalty_customers",
                        principalColumn: "phone_number",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "monthly_winners",
                columns: table => new
                {
                    winner_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    customer_phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    prize_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    draw_month = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    draw_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    tickets_held = table.Column<int>(type: "integer", nullable: false),
                    claimed = table.Column<bool>(type: "boolean", nullable: false),
                    claimed_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_monthly_winners", x => x.winner_id);
                    table.ForeignKey(
                        name: "FK_monthly_winners_loyalty_customers_customer_phone",
                        column: x => x.customer_phone,
                        principalTable: "loyalty_customers",
                        principalColumn: "phone_number",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "weekly_winners",
                columns: table => new
                {
                    winner_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    customer_phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    prize_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    draw_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    draw_week = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    tickets_held = table.Column<int>(type: "integer", nullable: false),
                    claimed = table.Column<bool>(type: "boolean", nullable: false),
                    claimed_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_weekly_winners", x => x.winner_id);
                    table.ForeignKey(
                        name: "FK_weekly_winners_loyalty_customers_customer_phone",
                        column: x => x.customer_phone,
                        principalTable: "loyalty_customers",
                        principalColumn: "phone_number",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_loyalty_customers_phone_number",
                table: "loyalty_customers",
                column: "phone_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_loyalty_customers_total_tickets_current_month",
                table: "loyalty_customers",
                column: "total_tickets_current_month");

            migrationBuilder.CreateIndex(
                name: "IX_loyalty_tickets_customer_phone",
                table: "loyalty_tickets",
                column: "customer_phone");

            migrationBuilder.CreateIndex(
                name: "IX_loyalty_tickets_customer_phone_draw_month",
                table: "loyalty_tickets",
                columns: new[] { "customer_phone", "draw_month" });

            migrationBuilder.CreateIndex(
                name: "IX_loyalty_tickets_draw_month",
                table: "loyalty_tickets",
                column: "draw_month");

            migrationBuilder.CreateIndex(
                name: "IX_loyalty_tickets_draw_month_is_valid",
                table: "loyalty_tickets",
                columns: new[] { "draw_month", "is_valid" });

            migrationBuilder.CreateIndex(
                name: "IX_loyalty_tickets_transaction_id",
                table: "loyalty_tickets",
                column: "transaction_id");

            migrationBuilder.CreateIndex(
                name: "IX_monthly_winners_customer_phone",
                table: "monthly_winners",
                column: "customer_phone");

            migrationBuilder.CreateIndex(
                name: "IX_monthly_winners_draw_date",
                table: "monthly_winners",
                column: "draw_date");

            migrationBuilder.CreateIndex(
                name: "IX_monthly_winners_draw_month",
                table: "monthly_winners",
                column: "draw_month");

            migrationBuilder.CreateIndex(
                name: "IX_weekly_winners_customer_phone",
                table: "weekly_winners",
                column: "customer_phone");

            migrationBuilder.CreateIndex(
                name: "IX_weekly_winners_draw_date",
                table: "weekly_winners",
                column: "draw_date");

            migrationBuilder.CreateIndex(
                name: "IX_weekly_winners_draw_week",
                table: "weekly_winners",
                column: "draw_week");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "loyalty_tickets");

            migrationBuilder.DropTable(
                name: "monthly_winners");

            migrationBuilder.DropTable(
                name: "weekly_winners");

            migrationBuilder.DropTable(
                name: "loyalty_customers");
        }
    }
}
