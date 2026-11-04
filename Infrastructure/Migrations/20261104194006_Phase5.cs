using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase5 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Ai_ImportDrafts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Kind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    FileName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Json = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Result = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    AppliedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Ai_ImportDrafts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Alert_Rules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Metric = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Comparator = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Threshold = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    WindowMinutes = table.Column<int>(type: "integer", nullable: false),
                    FromHour = table.Column<int>(type: "integer", nullable: false),
                    ToHour = table.Column<int>(type: "integer", nullable: false),
                    Channels = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Recipients = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CooldownMinutes = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    LastFiredOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Alert_Rules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Booking_StationBookings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SetId = table.Column<int>(type: "integer", nullable: false),
                    StartsOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndsOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CustomerName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Players = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Token = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Source = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CheckedInOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    HandledBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    BranchId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Booking_StationBookings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Booking_StationBookings_Sets_SetId",
                        column: x => x.SetId,
                        principalTable: "Sets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OwnerBot_Chats",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Channel = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    ChatId = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    ConversationId = table.Column<int>(type: "integer", nullable: true),
                    IsAllowed = table.Column<bool>(type: "boolean", nullable: false),
                    FirstSeenOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastMessageOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OwnerBot_Chats", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Parent_Links",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ChildUserId = table.Column<int>(type: "integer", nullable: false),
                    ParentName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ParentPhone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    ParentTelegramChatId = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    ParentEmail = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    DailyLimit = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    LowBalanceThreshold = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    NotifyOnSpend = table.Column<bool>(type: "boolean", nullable: false),
                    NotifyOnSessionEnd = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Token = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    LastLowBalanceAlertOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastNotifiedTransactionId = table.Column<int>(type: "integer", nullable: false),
                    LastNotifiedWalletTxId = table.Column<int>(type: "integer", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Parent_Links", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Station_Codes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SetId = table.Column<int>(type: "integer", nullable: false),
                    Token = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Station_Codes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Station_Codes_Sets_SetId",
                        column: x => x.SetId,
                        principalTable: "Sets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Tour_Tournaments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    GameTitle = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Rules = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    StartsOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    MaxPlayers = table.Column<int>(type: "integer", nullable: false),
                    EntryFee = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    PrizePool = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    PrizeSplit = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    RoomIds = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PublicToken = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    PrizesPaid = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    BranchId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tour_Tournaments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Alert_Events",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RuleId = table.Column<int>(type: "integer", nullable: false),
                    Value = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Delivery = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    FiredOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Alert_Events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Alert_Events_Alert_Rules_RuleId",
                        column: x => x.RuleId,
                        principalTable: "Alert_Rules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Tour_Matches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TournamentId = table.Column<int>(type: "integer", nullable: false),
                    Round = table.Column<int>(type: "integer", nullable: false),
                    Slot = table.Column<int>(type: "integer", nullable: false),
                    Player1Id = table.Column<int>(type: "integer", nullable: true),
                    Player2Id = table.Column<int>(type: "integer", nullable: true),
                    WinnerId = table.Column<int>(type: "integer", nullable: true),
                    Score = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    SetId = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    CompletedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tour_Matches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Tour_Matches_Tour_Tournaments_TournamentId",
                        column: x => x.TournamentId,
                        principalTable: "Tour_Tournaments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Tour_Participants",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TournamentId = table.Column<int>(type: "integer", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    UserId = table.Column<int>(type: "integer", nullable: true),
                    Paid = table.Column<bool>(type: "boolean", nullable: false),
                    PaidVia = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Seed = table.Column<int>(type: "integer", nullable: true),
                    FinalPlace = table.Column<int>(type: "integer", nullable: true),
                    PrizeAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tour_Participants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Tour_Participants_Tour_Tournaments_TournamentId",
                        column: x => x.TournamentId,
                        principalTable: "Tour_Tournaments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Ai_ImportDrafts_CreatedOn",
                table: "Ai_ImportDrafts",
                column: "CreatedOn");

            migrationBuilder.CreateIndex(
                name: "IX_Alert_Events_RuleId_FiredOn",
                table: "Alert_Events",
                columns: new[] { "RuleId", "FiredOn" });

            migrationBuilder.CreateIndex(
                name: "IX_Alert_Rules_IsActive",
                table: "Alert_Rules",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Booking_StationBookings_SetId_StartsOn",
                table: "Booking_StationBookings",
                columns: new[] { "SetId", "StartsOn" });

            migrationBuilder.CreateIndex(
                name: "IX_Booking_StationBookings_Token",
                table: "Booking_StationBookings",
                column: "Token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OwnerBot_Chats_Channel_ChatId",
                table: "OwnerBot_Chats",
                columns: new[] { "Channel", "ChatId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Parent_Links_ChildUserId",
                table: "Parent_Links",
                column: "ChildUserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Parent_Links_Token",
                table: "Parent_Links",
                column: "Token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Station_Codes_SetId",
                table: "Station_Codes",
                column: "SetId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Station_Codes_Token",
                table: "Station_Codes",
                column: "Token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tour_Matches_TournamentId_Round_Slot",
                table: "Tour_Matches",
                columns: new[] { "TournamentId", "Round", "Slot" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tour_Participants_TournamentId",
                table: "Tour_Participants",
                column: "TournamentId");

            migrationBuilder.CreateIndex(
                name: "IX_Tour_Tournaments_PublicToken",
                table: "Tour_Tournaments",
                column: "PublicToken",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tour_Tournaments_StartsOn",
                table: "Tour_Tournaments",
                column: "StartsOn");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Ai_ImportDrafts");

            migrationBuilder.DropTable(
                name: "Alert_Events");

            migrationBuilder.DropTable(
                name: "Booking_StationBookings");

            migrationBuilder.DropTable(
                name: "OwnerBot_Chats");

            migrationBuilder.DropTable(
                name: "Parent_Links");

            migrationBuilder.DropTable(
                name: "Station_Codes");

            migrationBuilder.DropTable(
                name: "Tour_Matches");

            migrationBuilder.DropTable(
                name: "Tour_Participants");

            migrationBuilder.DropTable(
                name: "Alert_Rules");

            migrationBuilder.DropTable(
                name: "Tour_Tournaments");
        }
    }
}
