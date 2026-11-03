using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class GapModules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Acct_AccountingPeriods",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    Month = table.Column<int>(type: "integer", nullable: false),
                    IsLocked = table.Column<bool>(type: "boolean", nullable: false),
                    LockedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LockedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Acct_AccountingPeriods", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Acct_BankAccounts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    BankName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    AccountNumberMasked = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    GlAccountId = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    BranchId = table.Column<int>(type: "integer", nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Acct_BankAccounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Acct_BankAccounts_accounts_GlAccountId",
                        column: x => x.GlAccountId,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Acct_FixedAssets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    AcquiredOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Cost = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    SalvageValue = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    UsefulLifeMonths = table.Column<int>(type: "integer", nullable: false),
                    Method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AssetGlAccountId = table.Column<int>(type: "integer", nullable: false),
                    AccumulatedDepGlAccountId = table.Column<int>(type: "integer", nullable: false),
                    DepExpenseGlAccountId = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DisposedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DisposalProceeds = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    DisposalJournalEntryId = table.Column<int>(type: "integer", nullable: true),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    BranchId = table.Column<int>(type: "integer", nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Acct_FixedAssets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Acct_FixedAssets_accounts_AccumulatedDepGlAccountId",
                        column: x => x.AccumulatedDepGlAccountId,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Acct_FixedAssets_accounts_AssetGlAccountId",
                        column: x => x.AssetGlAccountId,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Acct_FixedAssets_accounts_DepExpenseGlAccountId",
                        column: x => x.DepExpenseGlAccountId,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Acct_FixedAssets_journal_entries_DisposalJournalEntryId",
                        column: x => x.DisposalJournalEntryId,
                        principalTable: "journal_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Acct_SupplierBills",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SupplierId = table.Column<int>(type: "integer", nullable: false),
                    BillNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    BillDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DueDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Subtotal = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    TaxAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Total = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    PaidAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PurchaseId = table.Column<int>(type: "integer", nullable: true),
                    ExpenseGlAccountId = table.Column<int>(type: "integer", nullable: false),
                    PayableGlAccountId = table.Column<int>(type: "integer", nullable: false),
                    JournalEntryId = table.Column<int>(type: "integer", nullable: true),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    BranchId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Acct_SupplierBills", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Acct_SupplierBills_Purchases_PurchaseId",
                        column: x => x.PurchaseId,
                        principalTable: "Purchases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Acct_SupplierBills_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Acct_SupplierBills_accounts_ExpenseGlAccountId",
                        column: x => x.ExpenseGlAccountId,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Acct_SupplierBills_accounts_PayableGlAccountId",
                        column: x => x.PayableGlAccountId,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Acct_SupplierBills_journal_entries_JournalEntryId",
                        column: x => x.JournalEntryId,
                        principalTable: "journal_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Acct_TaxRates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RatePercent = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    AppliesTo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Acct_TaxRates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Bi_ScheduledReports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    ReportType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Frequency = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RunAtHourUtc = table.Column<int>(type: "integer", nullable: false),
                    DayOfWeek = table.Column<int>(type: "integer", nullable: true),
                    Recipients = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Channel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Format = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    LastRunOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastStatus = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ModifiedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    BranchId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bi_ScheduledReports", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Branch_Branches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Address = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false, defaultValue: "USD"),
                    TimeZoneId = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false, defaultValue: "Asia/Beirut"),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    OpeningTime = table.Column<TimeSpan>(type: "interval", nullable: true),
                    ClosingTime = table.Column<TimeSpan>(type: "interval", nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ModifiedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Branch_Branches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Currency_Currencies",
                columns: table => new
                {
                    Code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Symbol = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    DecimalPlaces = table.Column<int>(type: "integer", nullable: false),
                    IsBase = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    RoundingStep = table.Column<decimal>(type: "numeric(18,4)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Currency_Currencies", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "Delivery_Drivers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    VehicleNote = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    UserId = table.Column<int>(type: "integer", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    BranchId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Delivery_Drivers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Delivery_Zones",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Fee = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    MinOrder = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    EtaMinutes = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    BranchId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Delivery_Zones", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Floor_Areas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    BranchId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Floor_Areas", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Floor_BillSplits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TransactionId = table.Column<int>(type: "integer", nullable: false),
                    SplitNumber = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PaidOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PaidBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    BranchId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Floor_BillSplits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Floor_BillSplits_transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Floor_OrderCourses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TransactionId = table.Column<int>(type: "integer", nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    CourseNumber = table.Column<int>(type: "integer", nullable: false),
                    FiredOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FiredBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Floor_OrderCourses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Floor_OrderCourses_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Floor_OrderCourses_transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Floor_Tips",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TransactionId = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    StaffUserId = table.Column<int>(type: "integer", nullable: true),
                    StaffName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    BranchId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Floor_Tips", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Floor_Tips_transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Hr_Employees",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: true),
                    FullName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Position = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Department = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    HourlyRate = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    MonthlySalary = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    PinHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    PinSalt = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    HiredOn = table.Column<DateOnly>(type: "date", nullable: false),
                    TerminatedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    BranchId = table.Column<int>(type: "integer", nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Hr_Employees", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Hr_Employees_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Hr_LeaveTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IsPaid = table.Column<bool>(type: "boolean", nullable: false),
                    DefaultDaysPerYear = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Hr_LeaveTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Hr_ShiftTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    EndTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    Color = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Hr_ShiftTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Hr_TipPools",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DistributedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    BranchId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Hr_TipPools", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Offline_IdempotencyRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Endpoint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ResponseStatus = table.Column<int>(type: "integer", nullable: false),
                    ResponseBody = table.Column<string>(type: "text", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    BranchId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Offline_IdempotencyRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Offline_SyncBatches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ClientId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ReceivedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    OpsCount = table.Column<int>(type: "integer", nullable: false),
                    SucceededCount = table.Column<int>(type: "integer", nullable: false),
                    FailedCount = table.Column<int>(type: "integer", nullable: false),
                    ReceivedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    BranchId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Offline_SyncBatches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Promo_Combos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Price = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ImagePath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    BranchId = table.Column<int>(type: "integer", nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Promo_Combos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Promo_Coupons",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Value = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    MinOrderAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    MaxDiscountAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    MaxUses = table.Column<int>(type: "integer", nullable: true),
                    MaxUsesPerCustomer = table.Column<int>(type: "integer", nullable: true),
                    UsedCount = table.Column<int>(type: "integer", nullable: false),
                    ValidFrom = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ValidTo = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CategoryIdsCsv = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    BranchId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Promo_Coupons", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Promo_GiftCards",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    InitialAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Balance = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    IssuedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IssuedTo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    UserId = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    BranchId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Promo_GiftCards", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Promo_HappyHourRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    DaysOfWeekCsv = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    EndTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    DiscountPercent = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    CategoryIdsCsv = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ItemIdsCsv = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    BranchId = table.Column<int>(type: "integer", nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Promo_HappyHourRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SelfOrder_KioskDevices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Token = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    LastSeenOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    BranchId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SelfOrder_KioskDevices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SelfOrder_Sessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TableCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Token = table.Column<Guid>(type: "uuid", nullable: false),
                    TransactionId = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastActivityOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    GuestNote = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    BranchId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SelfOrder_Sessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SelfOrder_Sessions_transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "SelfOrder_Tables",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Label = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    BranchId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SelfOrder_Tables", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StockOps_Locations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    BranchId = table.Column<int>(type: "integer", nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockOps_Locations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StockOps_ReorderRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    IngredientId = table.Column<int>(type: "integer", nullable: false),
                    MinQty = table.Column<decimal>(type: "numeric(18,3)", nullable: false),
                    TargetQty = table.Column<decimal>(type: "numeric(18,3)", nullable: false),
                    PreferredSupplierId = table.Column<int>(type: "integer", nullable: true),
                    LeadTimeDays = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ModifiedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockOps_ReorderRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockOps_ReorderRules_Ingredients_IngredientId",
                        column: x => x.IngredientId,
                        principalTable: "Ingredients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StockOps_ReorderRules_Suppliers_PreferredSupplierId",
                        column: x => x.PreferredSupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "StockOps_StockBatches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    IngredientId = table.Column<int>(type: "integer", nullable: false),
                    PurchaseLineId = table.Column<int>(type: "integer", nullable: true),
                    LotNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Quantity = table.Column<decimal>(type: "numeric(18,3)", nullable: false),
                    RemainingQuantity = table.Column<decimal>(type: "numeric(18,3)", nullable: false),
                    ExpiresOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReceivedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    BranchId = table.Column<int>(type: "integer", nullable: true),
                    WrittenOffOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockOps_StockBatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockOps_StockBatches_Ingredients_IngredientId",
                        column: x => x.IngredientId,
                        principalTable: "Ingredients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StockOps_StockBatches_PurchaseLines_PurchaseLineId",
                        column: x => x.PurchaseLineId,
                        principalTable: "PurchaseLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Till_CashierShifts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RegisterName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    UserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    OpenedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ClosedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    OpeningFloat = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    ExpectedCash = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    CountedCash = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    Variance = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    ClosedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ReportJson = table.Column<string>(type: "text", nullable: true),
                    BranchId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Till_CashierShifts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Till_CashierShifts_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Till_VoidReasons",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RequiresApproval = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Till_VoidReasons", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Acct_BankStatements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BankAccountId = table.Column<int>(type: "integer", nullable: false),
                    PeriodFrom = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PeriodTo = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    OpeningBalance = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    ClosingBalance = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    ImportedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ImportedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ReconciledOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    BranchId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Acct_BankStatements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Acct_BankStatements_Acct_BankAccounts_BankAccountId",
                        column: x => x.BankAccountId,
                        principalTable: "Acct_BankAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Acct_DepreciationEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AssetId = table.Column<int>(type: "integer", nullable: false),
                    PeriodYear = table.Column<int>(type: "integer", nullable: false),
                    PeriodMonth = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    JournalEntryId = table.Column<int>(type: "integer", nullable: true),
                    PostedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PostedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Acct_DepreciationEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Acct_DepreciationEntries_Acct_FixedAssets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Acct_FixedAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Acct_DepreciationEntries_journal_entries_JournalEntryId",
                        column: x => x.JournalEntryId,
                        principalTable: "journal_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Acct_SupplierBillPayments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BillId = table.Column<int>(type: "integer", nullable: false),
                    PaidOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Method = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CashGlAccountId = table.Column<int>(type: "integer", nullable: false),
                    JournalEntryId = table.Column<int>(type: "integer", nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Acct_SupplierBillPayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Acct_SupplierBillPayments_Acct_SupplierBills_BillId",
                        column: x => x.BillId,
                        principalTable: "Acct_SupplierBills",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Acct_SupplierBillPayments_accounts_CashGlAccountId",
                        column: x => x.CashGlAccountId,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Acct_SupplierBillPayments_journal_entries_JournalEntryId",
                        column: x => x.JournalEntryId,
                        principalTable: "journal_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Bi_ReportRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ScheduledReportId = table.Column<int>(type: "integer", nullable: false),
                    RanOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Error = table.Column<string>(type: "text", nullable: true),
                    PayloadPreview = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    IsManual = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bi_ReportRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Bi_ReportRuns_Bi_ScheduledReports_ScheduledReportId",
                        column: x => x.ScheduledReportId,
                        principalTable: "Bi_ScheduledReports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Branch_Settings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BranchId = table.Column<int>(type: "integer", nullable: false),
                    Key = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Value = table.Column<string>(type: "text", nullable: true),
                    UpdatedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Branch_Settings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Branch_Settings_Branch_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branch_Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Branch_UserBranches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    BranchId = table.Column<int>(type: "integer", nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Branch_UserBranches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Branch_UserBranches_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Branch_UserBranches_Branch_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branch_Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Currency_ExchangeRates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Rate = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Currency_ExchangeRates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Currency_ExchangeRates_Currency_Currencies_CurrencyCode",
                        column: x => x.CurrencyCode,
                        principalTable: "Currency_Currencies",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Delivery_Orders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TransactionId = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ZoneId = table.Column<int>(type: "integer", nullable: true),
                    DriverId = table.Column<int>(type: "integer", nullable: true),
                    CustomerName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    DeliveryFee = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ScheduledFor = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AssignedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PickedUpOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeliveredOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ExternalSource = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    ExternalRef = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    BranchId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Delivery_Orders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Delivery_Orders_Delivery_Drivers_DriverId",
                        column: x => x.DriverId,
                        principalTable: "Delivery_Drivers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Delivery_Orders_Delivery_Zones_ZoneId",
                        column: x => x.ZoneId,
                        principalTable: "Delivery_Zones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Delivery_Orders_transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Floor_Tables",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AreaId = table.Column<int>(type: "integer", nullable: false),
                    Code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Capacity = table.Column<int>(type: "integer", nullable: false),
                    Shape = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    PosX = table.Column<int>(type: "integer", nullable: false),
                    PosY = table.Column<int>(type: "integer", nullable: false),
                    Width = table.Column<int>(type: "integer", nullable: false),
                    Height = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    BranchId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Floor_Tables", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Floor_Tables_Floor_Areas_AreaId",
                        column: x => x.AreaId,
                        principalTable: "Floor_Areas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Hr_LeaveRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EmployeeId = table.Column<int>(type: "integer", nullable: false),
                    LeaveTypeId = table.Column<int>(type: "integer", nullable: false),
                    FromDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ToDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Days = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RequestedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DecidedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    DecidedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DecisionNote = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Hr_LeaveRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Hr_LeaveRequests_Hr_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Hr_Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Hr_LeaveRequests_Hr_LeaveTypes_LeaveTypeId",
                        column: x => x.LeaveTypeId,
                        principalTable: "Hr_LeaveTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Hr_ScheduledShifts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EmployeeId = table.Column<int>(type: "integer", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    StartOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TemplateId = table.Column<int>(type: "integer", nullable: true),
                    Role = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    BranchId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Hr_ScheduledShifts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Hr_ScheduledShifts_Hr_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Hr_Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Hr_ScheduledShifts_Hr_ShiftTemplates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "Hr_ShiftTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Hr_TipPoolAllocations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TipPoolId = table.Column<int>(type: "integer", nullable: false),
                    EmployeeId = table.Column<int>(type: "integer", nullable: false),
                    Points = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    Hours = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Hr_TipPoolAllocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Hr_TipPoolAllocations_Hr_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Hr_Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Hr_TipPoolAllocations_Hr_TipPools_TipPoolId",
                        column: x => x.TipPoolId,
                        principalTable: "Hr_TipPools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Promo_ComboItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ComboId = table.Column<int>(type: "integer", nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Promo_ComboItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Promo_ComboItems_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Promo_ComboItems_Promo_Combos_ComboId",
                        column: x => x.ComboId,
                        principalTable: "Promo_Combos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Promo_CouponRedemptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CouponId = table.Column<int>(type: "integer", nullable: false),
                    TransactionId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: true),
                    DiscountAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    RedeemedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RedeemedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Promo_CouponRedemptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Promo_CouponRedemptions_Promo_Coupons_CouponId",
                        column: x => x.CouponId,
                        principalTable: "Promo_Coupons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Promo_CouponRedemptions_transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Promo_GiftCardTransactions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GiftCardId = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    TransactionId = table.Column<int>(type: "integer", nullable: true),
                    BalanceAfter = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    On = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    By = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Promo_GiftCardTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Promo_GiftCardTransactions_Promo_GiftCards_GiftCardId",
                        column: x => x.GiftCardId,
                        principalTable: "Promo_GiftCards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Promo_GiftCardTransactions_transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Promo_HappyHourApplications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RuleId = table.Column<int>(type: "integer", nullable: false),
                    TransactionId = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    On = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    By = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Promo_HappyHourApplications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Promo_HappyHourApplications_Promo_HappyHourRules_RuleId",
                        column: x => x.RuleId,
                        principalTable: "Promo_HappyHourRules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Promo_HappyHourApplications_transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StockOps_LocationStocks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LocationId = table.Column<int>(type: "integer", nullable: false),
                    IngredientId = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,3)", nullable: false),
                    ModifiedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockOps_LocationStocks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockOps_LocationStocks_Ingredients_IngredientId",
                        column: x => x.IngredientId,
                        principalTable: "Ingredients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StockOps_LocationStocks_StockOps_Locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "StockOps_Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StockOps_StockCounts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LocationId = table.Column<int>(type: "integer", nullable: false),
                    Reference = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    PostedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PostedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    BranchId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockOps_StockCounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockOps_StockCounts_StockOps_Locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "StockOps_Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockOps_StockTransfers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FromLocationId = table.Column<int>(type: "integer", nullable: false),
                    ToLocationId = table.Column<int>(type: "integer", nullable: false),
                    Reference = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    PostedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    BranchId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockOps_StockTransfers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockOps_StockTransfers_StockOps_Locations_FromLocationId",
                        column: x => x.FromLocationId,
                        principalTable: "StockOps_Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockOps_StockTransfers_StockOps_Locations_ToLocationId",
                        column: x => x.ToLocationId,
                        principalTable: "StockOps_Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Till_CashMovements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ShiftId = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Till_CashMovements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Till_CashMovements_Till_CashierShifts_ShiftId",
                        column: x => x.ShiftId,
                        principalTable: "Till_CashierShifts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Till_PaymentTenders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TransactionId = table.Column<int>(type: "integer", nullable: false),
                    ShiftId = table.Column<int>(type: "integer", nullable: true),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    AmountInCurrency = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    RateUsed = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    AmountInBase = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    BranchId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Till_PaymentTenders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Till_PaymentTenders_Currency_Currencies_CurrencyCode",
                        column: x => x.CurrencyCode,
                        principalTable: "Currency_Currencies",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Till_PaymentTenders_Till_CashierShifts_ShiftId",
                        column: x => x.ShiftId,
                        principalTable: "Till_CashierShifts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Till_PaymentTenders_transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Till_VoidRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TransactionId = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    ReasonId = table.Column<int>(type: "integer", nullable: false),
                    ReasonText = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    RequestedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    RequestedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    DecidedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    DecidedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DecisionNote = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    BranchId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Till_VoidRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Till_VoidRequests_Till_VoidReasons_ReasonId",
                        column: x => x.ReasonId,
                        principalTable: "Till_VoidReasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Acct_BankStatementLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StatementId = table.Column<int>(type: "integer", nullable: false),
                    Date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    MatchedJournalEntryLineId = table.Column<int>(type: "integer", nullable: true),
                    MatchStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    MatchedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    MatchedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Acct_BankStatementLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Acct_BankStatementLines_Acct_BankStatements_StatementId",
                        column: x => x.StatementId,
                        principalTable: "Acct_BankStatements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Acct_BankStatementLines_journal_entry_lines_MatchedJournalE~",
                        column: x => x.MatchedJournalEntryLineId,
                        principalTable: "journal_entry_lines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Delivery_StatusEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DeliveryOrderId = table.Column<int>(type: "integer", nullable: false),
                    FromStatus = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    ToStatus = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    On = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    By = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Delivery_StatusEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Delivery_StatusEvents_Delivery_Orders_DeliveryOrderId",
                        column: x => x.DeliveryOrderId,
                        principalTable: "Delivery_Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Floor_Reservations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CustomerName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: true),
                    ResourceType = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    TableId = table.Column<int>(type: "integer", nullable: true),
                    RoomId = table.Column<int>(type: "integer", nullable: true),
                    GameId = table.Column<int>(type: "integer", nullable: true),
                    StartOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PartySize = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Source = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ConfirmedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SeatedTransactionId = table.Column<int>(type: "integer", nullable: true),
                    BranchId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Floor_Reservations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Floor_Reservations_Floor_Tables_TableId",
                        column: x => x.TableId,
                        principalTable: "Floor_Tables",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Floor_Reservations_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Floor_Reservations_Rooms_RoomId",
                        column: x => x.RoomId,
                        principalTable: "Rooms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Floor_Reservations_transactions_SeatedTransactionId",
                        column: x => x.SeatedTransactionId,
                        principalTable: "transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Floor_TableSessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TableId = table.Column<int>(type: "integer", nullable: false),
                    TransactionId = table.Column<int>(type: "integer", nullable: false),
                    GuestCount = table.Column<int>(type: "integer", nullable: false),
                    OpenedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ClosedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    OpenedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    BranchId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Floor_TableSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Floor_TableSessions_Floor_Tables_TableId",
                        column: x => x.TableId,
                        principalTable: "Floor_Tables",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Floor_TableSessions_transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Hr_AttendanceRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EmployeeId = table.Column<int>(type: "integer", nullable: false),
                    ClockIn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ClockOut = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ScheduledShiftId = table.Column<int>(type: "integer", nullable: true),
                    DeviceName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    WorkedMinutes = table.Column<int>(type: "integer", nullable: true),
                    BranchId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Hr_AttendanceRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Hr_AttendanceRecords_Hr_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Hr_Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Hr_AttendanceRecords_Hr_ScheduledShifts_ScheduledShiftId",
                        column: x => x.ScheduledShiftId,
                        principalTable: "Hr_ScheduledShifts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "StockOps_StockCountLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StockCountId = table.Column<int>(type: "integer", nullable: false),
                    IngredientId = table.Column<int>(type: "integer", nullable: false),
                    ExpectedQty = table.Column<decimal>(type: "numeric(18,3)", nullable: false),
                    CountedQty = table.Column<decimal>(type: "numeric(18,3)", nullable: true),
                    Variance = table.Column<decimal>(type: "numeric(18,3)", nullable: false),
                    UnitCost = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    VarianceValue = table.Column<decimal>(type: "numeric(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockOps_StockCountLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockOps_StockCountLines_Ingredients_IngredientId",
                        column: x => x.IngredientId,
                        principalTable: "Ingredients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StockOps_StockCountLines_StockOps_StockCounts_StockCountId",
                        column: x => x.StockCountId,
                        principalTable: "StockOps_StockCounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StockOps_StockTransferLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TransferId = table.Column<int>(type: "integer", nullable: false),
                    IngredientId = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockOps_StockTransferLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockOps_StockTransferLines_Ingredients_IngredientId",
                        column: x => x.IngredientId,
                        principalTable: "Ingredients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StockOps_StockTransferLines_StockOps_StockTransfers_Transfe~",
                        column: x => x.TransferId,
                        principalTable: "StockOps_StockTransfers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Acct_AccountingPeriods_Year_Month",
                table: "Acct_AccountingPeriods",
                columns: new[] { "Year", "Month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Acct_BankAccounts_GlAccountId",
                table: "Acct_BankAccounts",
                column: "GlAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Acct_BankAccounts_Name",
                table: "Acct_BankAccounts",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Acct_BankStatementLines_MatchedJournalEntryLineId",
                table: "Acct_BankStatementLines",
                column: "MatchedJournalEntryLineId");

            migrationBuilder.CreateIndex(
                name: "IX_Acct_BankStatementLines_StatementId_MatchStatus",
                table: "Acct_BankStatementLines",
                columns: new[] { "StatementId", "MatchStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_Acct_BankStatements_BankAccountId_PeriodTo",
                table: "Acct_BankStatements",
                columns: new[] { "BankAccountId", "PeriodTo" });

            migrationBuilder.CreateIndex(
                name: "IX_Acct_DepreciationEntries_AssetId_PeriodYear_PeriodMonth",
                table: "Acct_DepreciationEntries",
                columns: new[] { "AssetId", "PeriodYear", "PeriodMonth" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Acct_DepreciationEntries_JournalEntryId",
                table: "Acct_DepreciationEntries",
                column: "JournalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_Acct_FixedAssets_AccumulatedDepGlAccountId",
                table: "Acct_FixedAssets",
                column: "AccumulatedDepGlAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Acct_FixedAssets_AssetGlAccountId",
                table: "Acct_FixedAssets",
                column: "AssetGlAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Acct_FixedAssets_DepExpenseGlAccountId",
                table: "Acct_FixedAssets",
                column: "DepExpenseGlAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Acct_FixedAssets_DisposalJournalEntryId",
                table: "Acct_FixedAssets",
                column: "DisposalJournalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_Acct_FixedAssets_Status",
                table: "Acct_FixedAssets",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Acct_SupplierBillPayments_BillId",
                table: "Acct_SupplierBillPayments",
                column: "BillId");

            migrationBuilder.CreateIndex(
                name: "IX_Acct_SupplierBillPayments_CashGlAccountId",
                table: "Acct_SupplierBillPayments",
                column: "CashGlAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Acct_SupplierBillPayments_JournalEntryId",
                table: "Acct_SupplierBillPayments",
                column: "JournalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_Acct_SupplierBillPayments_PaidOn",
                table: "Acct_SupplierBillPayments",
                column: "PaidOn");

            migrationBuilder.CreateIndex(
                name: "IX_Acct_SupplierBills_ExpenseGlAccountId",
                table: "Acct_SupplierBills",
                column: "ExpenseGlAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Acct_SupplierBills_JournalEntryId",
                table: "Acct_SupplierBills",
                column: "JournalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_Acct_SupplierBills_PayableGlAccountId",
                table: "Acct_SupplierBills",
                column: "PayableGlAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Acct_SupplierBills_PurchaseId",
                table: "Acct_SupplierBills",
                column: "PurchaseId");

            migrationBuilder.CreateIndex(
                name: "IX_Acct_SupplierBills_Status_DueDate",
                table: "Acct_SupplierBills",
                columns: new[] { "Status", "DueDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Acct_SupplierBills_SupplierId_BillNumber",
                table: "Acct_SupplierBills",
                columns: new[] { "SupplierId", "BillNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Acct_TaxRates_Name",
                table: "Acct_TaxRates",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Bi_ReportRuns_ScheduledReportId_RanOn",
                table: "Bi_ReportRuns",
                columns: new[] { "ScheduledReportId", "RanOn" });

            migrationBuilder.CreateIndex(
                name: "IX_Bi_ScheduledReports_BranchId",
                table: "Bi_ScheduledReports",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_Bi_ScheduledReports_IsActive_RunAtHourUtc",
                table: "Bi_ScheduledReports",
                columns: new[] { "IsActive", "RunAtHourUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Branch_Branches_Code",
                table: "Branch_Branches",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Branch_Branches_IsDefault",
                table: "Branch_Branches",
                column: "IsDefault");

            migrationBuilder.CreateIndex(
                name: "IX_Branch_Settings_BranchId_Key",
                table: "Branch_Settings",
                columns: new[] { "BranchId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Branch_UserBranches_BranchId",
                table: "Branch_UserBranches",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_Branch_UserBranches_UserId_BranchId",
                table: "Branch_UserBranches",
                columns: new[] { "UserId", "BranchId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Currency_ExchangeRates_CurrencyCode_EffectiveFrom",
                table: "Currency_ExchangeRates",
                columns: new[] { "CurrencyCode", "EffectiveFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_Delivery_Drivers_BranchId",
                table: "Delivery_Drivers",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_Delivery_Drivers_UserId",
                table: "Delivery_Drivers",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Delivery_Orders_BranchId",
                table: "Delivery_Orders",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_Delivery_Orders_CreatedOn",
                table: "Delivery_Orders",
                column: "CreatedOn");

            migrationBuilder.CreateIndex(
                name: "IX_Delivery_Orders_DriverId",
                table: "Delivery_Orders",
                column: "DriverId");

            migrationBuilder.CreateIndex(
                name: "IX_Delivery_Orders_ExternalSource_ExternalRef",
                table: "Delivery_Orders",
                columns: new[] { "ExternalSource", "ExternalRef" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Delivery_Orders_Status",
                table: "Delivery_Orders",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Delivery_Orders_TransactionId",
                table: "Delivery_Orders",
                column: "TransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_Delivery_Orders_ZoneId",
                table: "Delivery_Orders",
                column: "ZoneId");

            migrationBuilder.CreateIndex(
                name: "IX_Delivery_StatusEvents_DeliveryOrderId_On",
                table: "Delivery_StatusEvents",
                columns: new[] { "DeliveryOrderId", "On" });

            migrationBuilder.CreateIndex(
                name: "IX_Delivery_Zones_BranchId",
                table: "Delivery_Zones",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_Delivery_Zones_Name",
                table: "Delivery_Zones",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Floor_Areas_BranchId_SortOrder",
                table: "Floor_Areas",
                columns: new[] { "BranchId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_Floor_BillSplits_TransactionId",
                table: "Floor_BillSplits",
                column: "TransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_Floor_OrderCourses_ItemId",
                table: "Floor_OrderCourses",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_Floor_OrderCourses_TransactionId_ItemId",
                table: "Floor_OrderCourses",
                columns: new[] { "TransactionId", "ItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Floor_Reservations_GameId",
                table: "Floor_Reservations",
                column: "GameId");

            migrationBuilder.CreateIndex(
                name: "IX_Floor_Reservations_Phone",
                table: "Floor_Reservations",
                column: "Phone");

            migrationBuilder.CreateIndex(
                name: "IX_Floor_Reservations_RoomId",
                table: "Floor_Reservations",
                column: "RoomId");

            migrationBuilder.CreateIndex(
                name: "IX_Floor_Reservations_SeatedTransactionId",
                table: "Floor_Reservations",
                column: "SeatedTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_Floor_Reservations_StartOn_Status",
                table: "Floor_Reservations",
                columns: new[] { "StartOn", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Floor_Reservations_TableId",
                table: "Floor_Reservations",
                column: "TableId");

            migrationBuilder.CreateIndex(
                name: "IX_Floor_Tables_AreaId",
                table: "Floor_Tables",
                column: "AreaId");

            migrationBuilder.CreateIndex(
                name: "IX_Floor_Tables_Code",
                table: "Floor_Tables",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Floor_TableSessions_TableId_Status",
                table: "Floor_TableSessions",
                columns: new[] { "TableId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Floor_TableSessions_TransactionId",
                table: "Floor_TableSessions",
                column: "TransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_Floor_Tips_CreatedOn",
                table: "Floor_Tips",
                column: "CreatedOn");

            migrationBuilder.CreateIndex(
                name: "IX_Floor_Tips_TransactionId",
                table: "Floor_Tips",
                column: "TransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_Hr_AttendanceRecords_ClockIn",
                table: "Hr_AttendanceRecords",
                column: "ClockIn");

            migrationBuilder.CreateIndex(
                name: "IX_Hr_AttendanceRecords_EmployeeId_ClockIn",
                table: "Hr_AttendanceRecords",
                columns: new[] { "EmployeeId", "ClockIn" });

            migrationBuilder.CreateIndex(
                name: "IX_Hr_AttendanceRecords_ScheduledShiftId",
                table: "Hr_AttendanceRecords",
                column: "ScheduledShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_Hr_Employees_FullName",
                table: "Hr_Employees",
                column: "FullName");

            migrationBuilder.CreateIndex(
                name: "IX_Hr_Employees_Phone",
                table: "Hr_Employees",
                column: "Phone");

            migrationBuilder.CreateIndex(
                name: "IX_Hr_Employees_UserId",
                table: "Hr_Employees",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Hr_LeaveRequests_EmployeeId_FromDate",
                table: "Hr_LeaveRequests",
                columns: new[] { "EmployeeId", "FromDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Hr_LeaveRequests_LeaveTypeId",
                table: "Hr_LeaveRequests",
                column: "LeaveTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_Hr_LeaveRequests_Status",
                table: "Hr_LeaveRequests",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Hr_LeaveTypes_Name",
                table: "Hr_LeaveTypes",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Hr_ScheduledShifts_Date",
                table: "Hr_ScheduledShifts",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_Hr_ScheduledShifts_EmployeeId_Date",
                table: "Hr_ScheduledShifts",
                columns: new[] { "EmployeeId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_Hr_ScheduledShifts_TemplateId",
                table: "Hr_ScheduledShifts",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_Hr_ShiftTemplates_Name",
                table: "Hr_ShiftTemplates",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Hr_TipPoolAllocations_EmployeeId",
                table: "Hr_TipPoolAllocations",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_Hr_TipPoolAllocations_TipPoolId_EmployeeId",
                table: "Hr_TipPoolAllocations",
                columns: new[] { "TipPoolId", "EmployeeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Hr_TipPools_Date",
                table: "Hr_TipPools",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_Offline_IdempotencyRecords_CreatedOn",
                table: "Offline_IdempotencyRecords",
                column: "CreatedOn");

            migrationBuilder.CreateIndex(
                name: "IX_Offline_IdempotencyRecords_Key",
                table: "Offline_IdempotencyRecords",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Offline_SyncBatches_ClientId",
                table: "Offline_SyncBatches",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_Offline_SyncBatches_ReceivedOn",
                table: "Offline_SyncBatches",
                column: "ReceivedOn");

            migrationBuilder.CreateIndex(
                name: "IX_Promo_ComboItems_ComboId_ItemId",
                table: "Promo_ComboItems",
                columns: new[] { "ComboId", "ItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Promo_ComboItems_ItemId",
                table: "Promo_ComboItems",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_Promo_Combos_IsActive",
                table: "Promo_Combos",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Promo_CouponRedemptions_CouponId_TransactionId",
                table: "Promo_CouponRedemptions",
                columns: new[] { "CouponId", "TransactionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Promo_CouponRedemptions_TransactionId",
                table: "Promo_CouponRedemptions",
                column: "TransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_Promo_CouponRedemptions_UserId",
                table: "Promo_CouponRedemptions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Promo_Coupons_Code",
                table: "Promo_Coupons",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Promo_Coupons_IsActive",
                table: "Promo_Coupons",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Promo_GiftCards_Code",
                table: "Promo_GiftCards",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Promo_GiftCards_Status",
                table: "Promo_GiftCards",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Promo_GiftCards_UserId",
                table: "Promo_GiftCards",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Promo_GiftCardTransactions_GiftCardId",
                table: "Promo_GiftCardTransactions",
                column: "GiftCardId");

            migrationBuilder.CreateIndex(
                name: "IX_Promo_GiftCardTransactions_TransactionId",
                table: "Promo_GiftCardTransactions",
                column: "TransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_Promo_HappyHourApplications_RuleId",
                table: "Promo_HappyHourApplications",
                column: "RuleId");

            migrationBuilder.CreateIndex(
                name: "IX_Promo_HappyHourApplications_TransactionId",
                table: "Promo_HappyHourApplications",
                column: "TransactionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Promo_HappyHourRules_IsActive",
                table: "Promo_HappyHourRules",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_SelfOrder_KioskDevices_Token",
                table: "SelfOrder_KioskDevices",
                column: "Token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SelfOrder_Sessions_TableCode",
                table: "SelfOrder_Sessions",
                column: "TableCode");

            migrationBuilder.CreateIndex(
                name: "IX_SelfOrder_Sessions_Token",
                table: "SelfOrder_Sessions",
                column: "Token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SelfOrder_Sessions_TransactionId",
                table: "SelfOrder_Sessions",
                column: "TransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_SelfOrder_Tables_BranchId",
                table: "SelfOrder_Tables",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_SelfOrder_Tables_Code",
                table: "SelfOrder_Tables",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockOps_Locations_Code",
                table: "StockOps_Locations",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockOps_Locations_IsDefault",
                table: "StockOps_Locations",
                column: "IsDefault");

            migrationBuilder.CreateIndex(
                name: "IX_StockOps_LocationStocks_IngredientId",
                table: "StockOps_LocationStocks",
                column: "IngredientId");

            migrationBuilder.CreateIndex(
                name: "IX_StockOps_LocationStocks_LocationId_IngredientId",
                table: "StockOps_LocationStocks",
                columns: new[] { "LocationId", "IngredientId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockOps_ReorderRules_IngredientId",
                table: "StockOps_ReorderRules",
                column: "IngredientId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockOps_ReorderRules_PreferredSupplierId",
                table: "StockOps_ReorderRules",
                column: "PreferredSupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_StockOps_StockBatches_ExpiresOn",
                table: "StockOps_StockBatches",
                column: "ExpiresOn");

            migrationBuilder.CreateIndex(
                name: "IX_StockOps_StockBatches_IngredientId",
                table: "StockOps_StockBatches",
                column: "IngredientId");

            migrationBuilder.CreateIndex(
                name: "IX_StockOps_StockBatches_PurchaseLineId",
                table: "StockOps_StockBatches",
                column: "PurchaseLineId");

            migrationBuilder.CreateIndex(
                name: "IX_StockOps_StockCountLines_IngredientId",
                table: "StockOps_StockCountLines",
                column: "IngredientId");

            migrationBuilder.CreateIndex(
                name: "IX_StockOps_StockCountLines_StockCountId_IngredientId",
                table: "StockOps_StockCountLines",
                columns: new[] { "StockCountId", "IngredientId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockOps_StockCounts_LocationId",
                table: "StockOps_StockCounts",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_StockOps_StockCounts_Status",
                table: "StockOps_StockCounts",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_StockOps_StockTransferLines_IngredientId",
                table: "StockOps_StockTransferLines",
                column: "IngredientId");

            migrationBuilder.CreateIndex(
                name: "IX_StockOps_StockTransferLines_TransferId",
                table: "StockOps_StockTransferLines",
                column: "TransferId");

            migrationBuilder.CreateIndex(
                name: "IX_StockOps_StockTransfers_FromLocationId",
                table: "StockOps_StockTransfers",
                column: "FromLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_StockOps_StockTransfers_Status",
                table: "StockOps_StockTransfers",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_StockOps_StockTransfers_ToLocationId",
                table: "StockOps_StockTransfers",
                column: "ToLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_Till_CashierShifts_BranchId",
                table: "Till_CashierShifts",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_Till_CashierShifts_OpenedOn",
                table: "Till_CashierShifts",
                column: "OpenedOn");

            migrationBuilder.CreateIndex(
                name: "IX_Till_CashierShifts_UserId_RegisterName_Status",
                table: "Till_CashierShifts",
                columns: new[] { "UserId", "RegisterName", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Till_CashMovements_ShiftId",
                table: "Till_CashMovements",
                column: "ShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_Till_PaymentTenders_BranchId",
                table: "Till_PaymentTenders",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_Till_PaymentTenders_CurrencyCode",
                table: "Till_PaymentTenders",
                column: "CurrencyCode");

            migrationBuilder.CreateIndex(
                name: "IX_Till_PaymentTenders_ShiftId",
                table: "Till_PaymentTenders",
                column: "ShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_Till_PaymentTenders_TransactionId",
                table: "Till_PaymentTenders",
                column: "TransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_Till_VoidReasons_Name",
                table: "Till_VoidReasons",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Till_VoidRequests_BranchId",
                table: "Till_VoidRequests",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_Till_VoidRequests_ReasonId",
                table: "Till_VoidRequests",
                column: "ReasonId");

            migrationBuilder.CreateIndex(
                name: "IX_Till_VoidRequests_Status",
                table: "Till_VoidRequests",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Till_VoidRequests_TransactionId",
                table: "Till_VoidRequests",
                column: "TransactionId");

}

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Acct_AccountingPeriods");

            migrationBuilder.DropTable(
                name: "Acct_BankStatementLines");

            migrationBuilder.DropTable(
                name: "Acct_DepreciationEntries");

            migrationBuilder.DropTable(
                name: "Acct_SupplierBillPayments");

            migrationBuilder.DropTable(
                name: "Acct_TaxRates");

            migrationBuilder.DropTable(
                name: "Bi_ReportRuns");

            migrationBuilder.DropTable(
                name: "Branch_Settings");

            migrationBuilder.DropTable(
                name: "Branch_UserBranches");

            migrationBuilder.DropTable(
                name: "Currency_ExchangeRates");

            migrationBuilder.DropTable(
                name: "Delivery_StatusEvents");

            migrationBuilder.DropTable(
                name: "Floor_BillSplits");

            migrationBuilder.DropTable(
                name: "Floor_OrderCourses");

            migrationBuilder.DropTable(
                name: "Floor_Reservations");

            migrationBuilder.DropTable(
                name: "Floor_TableSessions");

            migrationBuilder.DropTable(
                name: "Floor_Tips");

            migrationBuilder.DropTable(
                name: "Hr_AttendanceRecords");

            migrationBuilder.DropTable(
                name: "Hr_LeaveRequests");

            migrationBuilder.DropTable(
                name: "Hr_TipPoolAllocations");

            migrationBuilder.DropTable(
                name: "Offline_IdempotencyRecords");

            migrationBuilder.DropTable(
                name: "Offline_SyncBatches");

            migrationBuilder.DropTable(
                name: "Promo_ComboItems");

            migrationBuilder.DropTable(
                name: "Promo_CouponRedemptions");

            migrationBuilder.DropTable(
                name: "Promo_GiftCardTransactions");

            migrationBuilder.DropTable(
                name: "Promo_HappyHourApplications");

            migrationBuilder.DropTable(
                name: "SelfOrder_KioskDevices");

            migrationBuilder.DropTable(
                name: "SelfOrder_Sessions");

            migrationBuilder.DropTable(
                name: "SelfOrder_Tables");

            migrationBuilder.DropTable(
                name: "StockOps_LocationStocks");

            migrationBuilder.DropTable(
                name: "StockOps_ReorderRules");

            migrationBuilder.DropTable(
                name: "StockOps_StockBatches");

            migrationBuilder.DropTable(
                name: "StockOps_StockCountLines");

            migrationBuilder.DropTable(
                name: "StockOps_StockTransferLines");

            migrationBuilder.DropTable(
                name: "Till_CashMovements");

            migrationBuilder.DropTable(
                name: "Till_PaymentTenders");

            migrationBuilder.DropTable(
                name: "Till_VoidRequests");

            migrationBuilder.DropTable(
                name: "Acct_BankStatements");

            migrationBuilder.DropTable(
                name: "Acct_FixedAssets");

            migrationBuilder.DropTable(
                name: "Acct_SupplierBills");

            migrationBuilder.DropTable(
                name: "Bi_ScheduledReports");

            migrationBuilder.DropTable(
                name: "Branch_Branches");

            migrationBuilder.DropTable(
                name: "Delivery_Orders");

            migrationBuilder.DropTable(
                name: "Floor_Tables");

            migrationBuilder.DropTable(
                name: "Hr_ScheduledShifts");

            migrationBuilder.DropTable(
                name: "Hr_LeaveTypes");

            migrationBuilder.DropTable(
                name: "Hr_TipPools");

            migrationBuilder.DropTable(
                name: "Promo_Combos");

            migrationBuilder.DropTable(
                name: "Promo_Coupons");

            migrationBuilder.DropTable(
                name: "Promo_GiftCards");

            migrationBuilder.DropTable(
                name: "Promo_HappyHourRules");

            migrationBuilder.DropTable(
                name: "StockOps_StockCounts");

            migrationBuilder.DropTable(
                name: "StockOps_StockTransfers");

            migrationBuilder.DropTable(
                name: "Currency_Currencies");

            migrationBuilder.DropTable(
                name: "Till_CashierShifts");

            migrationBuilder.DropTable(
                name: "Till_VoidReasons");

            migrationBuilder.DropTable(
                name: "Acct_BankAccounts");

            migrationBuilder.DropTable(
                name: "Delivery_Drivers");

            migrationBuilder.DropTable(
                name: "Delivery_Zones");

            migrationBuilder.DropTable(
                name: "Floor_Areas");

            migrationBuilder.DropTable(
                name: "Hr_Employees");

            migrationBuilder.DropTable(
                name: "Hr_ShiftTemplates");

            migrationBuilder.DropTable(
                name: "StockOps_Locations");
        }
    }
}
