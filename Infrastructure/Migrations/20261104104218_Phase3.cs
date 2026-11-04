using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase3 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Hr_PayComponentTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Rate = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    IsStatutory = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    DefaultForAll = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Hr_PayComponentTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Hr_PayrollRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    Month = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    GeneratedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    GeneratedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ApprovedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ApprovedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    PaidOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PaidBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    TotalGross = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    TotalDeductions = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    TotalStatutory = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    TotalNet = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    JournalEntryId = table.Column<int>(type: "integer", nullable: true),
                    PaymentJournalEntryId = table.Column<int>(type: "integer", nullable: true),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    BranchId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Hr_PayrollRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Hr_PayrollRuns_journal_entries_JournalEntryId",
                        column: x => x.JournalEntryId,
                        principalTable: "journal_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Hr_PayrollRuns_journal_entries_PaymentJournalEntryId",
                        column: x => x.PaymentJournalEntryId,
                        principalTable: "journal_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Mkt_Segments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    RulesJson = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Mkt_Segments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Mkt_Unsubscribes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Address = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    Channel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    On = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Source = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Mkt_Unsubscribes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Promo_AccountingPostings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ReferenceType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ReferenceId = table.Column<int>(type: "integer", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    InvoiceId = table.Column<int>(type: "integer", nullable: true),
                    GiftCardId = table.Column<int>(type: "integer", nullable: true),
                    JournalEntryId = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    On = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PostedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    By = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    BranchId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Promo_AccountingPostings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Promo_AccountingPostings_journal_entries_JournalEntryId",
                        column: x => x.JournalEntryId,
                        principalTable: "journal_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Retail_Barcodes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    VariantId = table.Column<int>(type: "integer", nullable: true),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Symbology = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    IsPrimary = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Retail_Barcodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Retail_Barcodes_ItemVariants_VariantId",
                        column: x => x.VariantId,
                        principalTable: "ItemVariants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Retail_Barcodes_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Retail_SerialNumbers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    VariantId = table.Column<int>(type: "integer", nullable: true),
                    Serial = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    TransactionId = table.Column<int>(type: "integer", nullable: true),
                    ReceivedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SoldOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReturnedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    BranchId = table.Column<int>(type: "integer", nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ModifiedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Retail_SerialNumbers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Retail_SerialNumbers_ItemVariants_VariantId",
                        column: x => x.VariantId,
                        principalTable: "ItemVariants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Retail_SerialNumbers_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Retail_SerialNumbers_transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Retail_SerialSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    RequiresSerial = table.Column<bool>(type: "boolean", nullable: false),
                    Prefix = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ModifiedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Retail_SerialSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Retail_SerialSettings_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Hr_EmployeePayComponents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EmployeeId = table.Column<int>(type: "integer", nullable: false),
                    ComponentTypeId = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    Rate = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Hr_EmployeePayComponents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Hr_EmployeePayComponents_Hr_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Hr_Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Hr_EmployeePayComponents_Hr_PayComponentTypes_ComponentType~",
                        column: x => x.ComponentTypeId,
                        principalTable: "Hr_PayComponentTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Hr_PayrollAdvances",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EmployeeId = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    On = table.Column<DateOnly>(type: "date", nullable: false),
                    RecoveredInRunId = table.Column<int>(type: "integer", nullable: true),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    BranchId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Hr_PayrollAdvances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Hr_PayrollAdvances_Hr_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Hr_Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Hr_PayrollAdvances_Hr_PayrollRuns_RecoveredInRunId",
                        column: x => x.RecoveredInRunId,
                        principalTable: "Hr_PayrollRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Hr_PayrollLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RunId = table.Column<int>(type: "integer", nullable: false),
                    EmployeeId = table.Column<int>(type: "integer", nullable: false),
                    EmployeeName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Position = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    BasePay = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    HoursWorked = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    HourlyRate = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    MonthlySalary = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    OvertimeHours = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    OvertimePay = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Allowances = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Deductions = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    StatutoryDeductions = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    AdvancesRecovered = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    ManualAllowance = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    ManualDeduction = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Gross = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Net = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    ComponentsJson = table.Column<string>(type: "text", nullable: true),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsExcluded = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Hr_PayrollLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Hr_PayrollLines_Hr_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Hr_Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Hr_PayrollLines_Hr_PayrollRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "Hr_PayrollRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Mkt_Campaigns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Channel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SegmentId = table.Column<int>(type: "integer", nullable: false),
                    Subject = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Body = table.Column<string>(type: "text", nullable: false),
                    WhatsAppTemplate = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    PromoCode = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ScheduledFor = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SentOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AudienceCount = table.Column<int>(type: "integer", nullable: false),
                    SentCount = table.Column<int>(type: "integer", nullable: false),
                    FailedCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    BranchId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Mkt_Campaigns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Mkt_Campaigns_Mkt_Segments_SegmentId",
                        column: x => x.SegmentId,
                        principalTable: "Mkt_Segments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Mkt_CampaignRecipients",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CampaignId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: true),
                    Address = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    SentOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsTest = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Mkt_CampaignRecipients", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Mkt_CampaignRecipients_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Mkt_CampaignRecipients_Mkt_Campaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "Mkt_Campaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Hr_EmployeePayComponents_ComponentTypeId",
                table: "Hr_EmployeePayComponents",
                column: "ComponentTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_Hr_EmployeePayComponents_EmployeeId_ComponentTypeId",
                table: "Hr_EmployeePayComponents",
                columns: new[] { "EmployeeId", "ComponentTypeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Hr_PayComponentTypes_Name",
                table: "Hr_PayComponentTypes",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Hr_PayrollAdvances_EmployeeId_On",
                table: "Hr_PayrollAdvances",
                columns: new[] { "EmployeeId", "On" });

            migrationBuilder.CreateIndex(
                name: "IX_Hr_PayrollAdvances_RecoveredInRunId",
                table: "Hr_PayrollAdvances",
                column: "RecoveredInRunId");

            migrationBuilder.CreateIndex(
                name: "IX_Hr_PayrollLines_EmployeeId",
                table: "Hr_PayrollLines",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_Hr_PayrollLines_RunId_EmployeeId",
                table: "Hr_PayrollLines",
                columns: new[] { "RunId", "EmployeeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Hr_PayrollRuns_JournalEntryId",
                table: "Hr_PayrollRuns",
                column: "JournalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_Hr_PayrollRuns_PaymentJournalEntryId",
                table: "Hr_PayrollRuns",
                column: "PaymentJournalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_Hr_PayrollRuns_Year_Month",
                table: "Hr_PayrollRuns",
                columns: new[] { "Year", "Month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Mkt_CampaignRecipients_CampaignId_Status",
                table: "Mkt_CampaignRecipients",
                columns: new[] { "CampaignId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Mkt_CampaignRecipients_UserId",
                table: "Mkt_CampaignRecipients",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Mkt_Campaigns_BranchId",
                table: "Mkt_Campaigns",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_Mkt_Campaigns_SegmentId",
                table: "Mkt_Campaigns",
                column: "SegmentId");

            migrationBuilder.CreateIndex(
                name: "IX_Mkt_Campaigns_Status_ScheduledFor",
                table: "Mkt_Campaigns",
                columns: new[] { "Status", "ScheduledFor" });

            migrationBuilder.CreateIndex(
                name: "IX_Mkt_Segments_IsActive",
                table: "Mkt_Segments",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Mkt_Unsubscribes_Address",
                table: "Mkt_Unsubscribes",
                column: "Address",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Promo_AccountingPostings_InvoiceId",
                table: "Promo_AccountingPostings",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_Promo_AccountingPostings_JournalEntryId",
                table: "Promo_AccountingPostings",
                column: "JournalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_Promo_AccountingPostings_On",
                table: "Promo_AccountingPostings",
                column: "On");

            migrationBuilder.CreateIndex(
                name: "IX_Promo_AccountingPostings_ReferenceType_ReferenceId_Sequence",
                table: "Promo_AccountingPostings",
                columns: new[] { "ReferenceType", "ReferenceId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Promo_AccountingPostings_Status",
                table: "Promo_AccountingPostings",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Retail_Barcodes_Code",
                table: "Retail_Barcodes",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Retail_Barcodes_ItemId_VariantId",
                table: "Retail_Barcodes",
                columns: new[] { "ItemId", "VariantId" });

            migrationBuilder.CreateIndex(
                name: "IX_Retail_Barcodes_ItemId_VariantId_IsPrimary",
                table: "Retail_Barcodes",
                columns: new[] { "ItemId", "VariantId", "IsPrimary" },
                unique: true,
                filter: "\"IsPrimary\" = true");

            migrationBuilder.CreateIndex(
                name: "IX_Retail_Barcodes_VariantId",
                table: "Retail_Barcodes",
                column: "VariantId");

            migrationBuilder.CreateIndex(
                name: "IX_Retail_SerialNumbers_ItemId_Status",
                table: "Retail_SerialNumbers",
                columns: new[] { "ItemId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Retail_SerialNumbers_Serial",
                table: "Retail_SerialNumbers",
                column: "Serial",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Retail_SerialNumbers_TransactionId",
                table: "Retail_SerialNumbers",
                column: "TransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_Retail_SerialNumbers_VariantId",
                table: "Retail_SerialNumbers",
                column: "VariantId");

            migrationBuilder.CreateIndex(
                name: "IX_Retail_SerialSettings_ItemId",
                table: "Retail_SerialSettings",
                column: "ItemId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Hr_EmployeePayComponents");

            migrationBuilder.DropTable(
                name: "Hr_PayrollAdvances");

            migrationBuilder.DropTable(
                name: "Hr_PayrollLines");

            migrationBuilder.DropTable(
                name: "Mkt_CampaignRecipients");

            migrationBuilder.DropTable(
                name: "Mkt_Unsubscribes");

            migrationBuilder.DropTable(
                name: "Promo_AccountingPostings");

            migrationBuilder.DropTable(
                name: "Retail_Barcodes");

            migrationBuilder.DropTable(
                name: "Retail_SerialNumbers");

            migrationBuilder.DropTable(
                name: "Retail_SerialSettings");

            migrationBuilder.DropTable(
                name: "Hr_PayComponentTypes");

            migrationBuilder.DropTable(
                name: "Hr_PayrollRuns");

            migrationBuilder.DropTable(
                name: "Mkt_Campaigns");

            migrationBuilder.DropTable(
                name: "Mkt_Segments");
        }
    }
}
