using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase4 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Delivery_Platforms",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Source = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    StatusCallbackUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    PriceMarkupPercent = table.Column<decimal>(type: "numeric(9,2)", nullable: false),
                    AutoAccept = table.Column<bool>(type: "boolean", nullable: false),
                    MappedItemsOnly = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Delivery_Platforms", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Menu_ItemModels",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    GlbPath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    UsdzPath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    PosterPath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    GlbBytes = table.Column<long>(type: "bigint", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Menu_ItemModels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Menu_ItemModels_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Pay_CardTerminals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ProviderTerminalId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    BranchId = table.Column<int>(type: "integer", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Pay_CardTerminals", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Upsell_Events",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Channel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Action = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    SuggestItemId = table.Column<int>(type: "integer", nullable: false),
                    Source = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Price = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    BranchId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Upsell_Events", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Upsell_Pairs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    SuggestItemId = table.Column<int>(type: "integer", nullable: false),
                    PairCount = table.Column<int>(type: "integer", nullable: false),
                    Confidence = table.Column<decimal>(type: "numeric(9,4)", nullable: false),
                    Lift = table.Column<decimal>(type: "numeric(9,4)", nullable: false),
                    Pitch = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ComputedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Upsell_Pairs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Upsell_Pairs_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Upsell_Pairs_Items_SuggestItemId",
                        column: x => x.SuggestItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Upsell_Rules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TriggerItemId = table.Column<int>(type: "integer", nullable: true),
                    TriggerCategoryId = table.Column<int>(type: "integer", nullable: true),
                    SuggestItemId = table.Column<int>(type: "integer", nullable: false),
                    Message = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Upsell_Rules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Upsell_Rules_Categories_TriggerCategoryId",
                        column: x => x.TriggerCategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Upsell_Rules_Items_SuggestItemId",
                        column: x => x.SuggestItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Upsell_Rules_Items_TriggerItemId",
                        column: x => x.TriggerItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Delivery_ItemMappings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PlatformId = table.Column<int>(type: "integer", nullable: false),
                    ExternalItemId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    PriceOverride = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    IsAvailable = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Delivery_ItemMappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Delivery_ItemMappings_Delivery_Platforms_PlatformId",
                        column: x => x.PlatformId,
                        principalTable: "Delivery_Platforms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Delivery_ItemMappings_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Delivery_PlatformPushes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PlatformId = table.Column<int>(type: "integer", nullable: false),
                    DeliveryOrderId = table.Column<int>(type: "integer", nullable: false),
                    StatusEventId = table.Column<int>(type: "integer", nullable: false),
                    ExternalRef = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    State = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    LastHttpStatus = table.Column<int>(type: "integer", nullable: true),
                    LastError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SentOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NextAttemptOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Delivery_PlatformPushes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Delivery_PlatformPushes_Delivery_Orders_DeliveryOrderId",
                        column: x => x.DeliveryOrderId,
                        principalTable: "Delivery_Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Delivery_PlatformPushes_Delivery_Platforms_PlatformId",
                        column: x => x.PlatformId,
                        principalTable: "Delivery_Platforms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Pay_TerminalPayments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TransactionId = table.Column<int>(type: "integer", nullable: false),
                    TerminalId = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ApprovalCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    CardLast4 = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    CardScheme = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Rrn = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Message = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CompletedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastCheckedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Raw = table.Column<string>(type: "text", nullable: true),
                    BranchId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Pay_TerminalPayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Pay_TerminalPayments_Pay_CardTerminals_TerminalId",
                        column: x => x.TerminalId,
                        principalTable: "Pay_CardTerminals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Pay_TerminalPayments_transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Delivery_ItemMappings_ItemId",
                table: "Delivery_ItemMappings",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_Delivery_ItemMappings_PlatformId_ExternalItemId",
                table: "Delivery_ItemMappings",
                columns: new[] { "PlatformId", "ExternalItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Delivery_ItemMappings_PlatformId_ItemId",
                table: "Delivery_ItemMappings",
                columns: new[] { "PlatformId", "ItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_Delivery_PlatformPushes_DeliveryOrderId",
                table: "Delivery_PlatformPushes",
                column: "DeliveryOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_Delivery_PlatformPushes_PlatformId",
                table: "Delivery_PlatformPushes",
                column: "PlatformId");

            migrationBuilder.CreateIndex(
                name: "IX_Delivery_PlatformPushes_State_NextAttemptOn",
                table: "Delivery_PlatformPushes",
                columns: new[] { "State", "NextAttemptOn" });

            migrationBuilder.CreateIndex(
                name: "IX_Delivery_PlatformPushes_StatusEventId",
                table: "Delivery_PlatformPushes",
                column: "StatusEventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Delivery_Platforms_Source",
                table: "Delivery_Platforms",
                column: "Source",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Menu_ItemModels_ItemId",
                table: "Menu_ItemModels",
                column: "ItemId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Pay_CardTerminals_BranchId",
                table: "Pay_CardTerminals",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_Pay_TerminalPayments_CreatedOn",
                table: "Pay_TerminalPayments",
                column: "CreatedOn");

            migrationBuilder.CreateIndex(
                name: "IX_Pay_TerminalPayments_ExternalId",
                table: "Pay_TerminalPayments",
                column: "ExternalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Pay_TerminalPayments_Status",
                table: "Pay_TerminalPayments",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Pay_TerminalPayments_TerminalId",
                table: "Pay_TerminalPayments",
                column: "TerminalId");

            migrationBuilder.CreateIndex(
                name: "IX_Pay_TerminalPayments_TransactionId",
                table: "Pay_TerminalPayments",
                column: "TransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_Upsell_Events_CreatedOn",
                table: "Upsell_Events",
                column: "CreatedOn");

            migrationBuilder.CreateIndex(
                name: "IX_Upsell_Events_SuggestItemId",
                table: "Upsell_Events",
                column: "SuggestItemId");

            migrationBuilder.CreateIndex(
                name: "IX_Upsell_Pairs_ItemId_SuggestItemId",
                table: "Upsell_Pairs",
                columns: new[] { "ItemId", "SuggestItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Upsell_Pairs_SuggestItemId",
                table: "Upsell_Pairs",
                column: "SuggestItemId");

            migrationBuilder.CreateIndex(
                name: "IX_Upsell_Rules_SuggestItemId",
                table: "Upsell_Rules",
                column: "SuggestItemId");

            migrationBuilder.CreateIndex(
                name: "IX_Upsell_Rules_TriggerCategoryId",
                table: "Upsell_Rules",
                column: "TriggerCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Upsell_Rules_TriggerItemId",
                table: "Upsell_Rules",
                column: "TriggerItemId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Delivery_ItemMappings");

            migrationBuilder.DropTable(
                name: "Delivery_PlatformPushes");

            migrationBuilder.DropTable(
                name: "Menu_ItemModels");

            migrationBuilder.DropTable(
                name: "Pay_TerminalPayments");

            migrationBuilder.DropTable(
                name: "Upsell_Events");

            migrationBuilder.DropTable(
                name: "Upsell_Pairs");

            migrationBuilder.DropTable(
                name: "Upsell_Rules");

            migrationBuilder.DropTable(
                name: "Delivery_Platforms");

            migrationBuilder.DropTable(
                name: "Pay_CardTerminals");
        }
    }
}
