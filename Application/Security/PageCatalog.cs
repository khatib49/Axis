namespace Application.Security
{
    /// <summary>
    /// A dashboard page that can be granted to a role.
    /// Controllers = the API controllers that page calls; a role granted the
    /// page may call them (see DynamicPageAuthorizationHandler in the API).
    /// LegacyRoles = the built-in roles that historically had the page; used
    /// once, to seed their permissions.
    /// </summary>
    public sealed record PageDefinition(
        string Key, string Label, string Group, string Path,
        string[] Controllers, string[] LegacyRoles);

    /// <summary>
    /// The catalogue of dashboard pages. Keys are shared with the frontend
    /// (src/config/pages.ts) — keep both lists in step when adding a page.
    /// </summary>
    public static class PageCatalog
    {
        public const string AdminRole = "admin";

        /// <summary>Roles that ship with the system and can't be deleted.</summary>
        public static readonly string[] BuiltInRoles =
            { "admin", "cashier", "gamecashier", "admin_fnb", "chef", "stock", "bartender", "social_media" };

        private static readonly string[] None = Array.Empty<string>();
        private static readonly string[] Stock = { "chef", "admin_fnb", "stock" };
        private static readonly string[] TillRoles = { "cashier", "gamecashier", "admin_fnb" };
        private static readonly string[] GameTill = {
            "GameSessionController", "TransactionsController", "RoomController", "SettingController", "GameController",
            "KitchenController", "DiscountController", "WalletsController", "LoyaltyController", "ChannelController", "UserCardController" };
        private static readonly string[] FoodTill = {
            "TransactionsController", "KitchenController", "DiscountController", "WalletsController",
            "LoyaltyController", "ChannelController", "UserCardController", "SettingController" };

        public static readonly IReadOnlyList<PageDefinition> Pages = new[]
        {
            // ── Overview ────────────────────────────────────────────────
            new PageDefinition("dashboard", "Dashboard", "Overview", "/dashboard",
                new[] { "ProfitController", "TransactionsReportsController", "TransactionsController", "GameSessionController" }, None),
            new PageDefinition("menu", "Menu (public)", "Overview", "/menu", None, None),

            // ── People ──────────────────────────────────────────────────
            new PageDefinition("users", "Users Management", "People", "/admin/users",
                new[] { "UsersController", "AuthController", "RoleCategoryController" }, None),
            new PageDefinition("roles", "Roles & Permissions", "People", "/admin/roles",
                new[] { "RolesController" }, None),

            // ── Inventory ───────────────────────────────────────────────
            new PageDefinition("items", "Items", "Inventory Management", "/admin/items",
                new[] { "ItemController", "CategoryController", "SetController", "StatusController", "RoleCategoryController" }, None),
            new PageDefinition("categories", "Categories", "Inventory Management", "/admin/categories",
                new[] { "CategoryController", "RoleCategoryController" }, None),
            new PageDefinition("orders", "Orders", "Inventory Management", "/admin/orders",
                new[] { "TransactionsController", "KitchenController", "TransactionAuditLogController" }, None),
            new PageDefinition("qr-generator", "QR Generator", "Inventory Management", "/admin/qr-generator", None, None),

            // ── Stock ───────────────────────────────────────────────────
            new PageDefinition("stock-ingredients", "Ingredients", "Stock Management", "/chef/ingredients",
                new[] { "IngredientsController", "RecipesController", "StockController", "SuppliersController" }, Stock),
            new PageDefinition("stock-suppliers", "Suppliers", "Stock Management", "/chef/suppliers",
                new[] { "SuppliersController" }, Stock),
            new PageDefinition("stock-purchases", "Purchases", "Stock Management", "/chef/purchases",
                new[] { "PurchasesController", "SuppliersController", "IngredientsController" }, Stock),
            new PageDefinition("stock-valuation", "Inventory Valuation", "Stock Management", "/chef/inventory-valuation",
                new[] { "InventoryValuationController" }, Stock),
            new PageDefinition("stock-movements", "Stock Movements & Waste Log", "Stock Management", "/chef/stock-movements",
                new[] { "StockController", "IngredientsController" }, Stock),

            // ── Game ────────────────────────────────────────────────────
            new PageDefinition("game-overview", "Game Overview", "Game", "/admin/game",
                new[] { "GameController", "SettingController", "RoomController", "GameSessionController" }, None),
            new PageDefinition("game-settings", "Game Settings", "Game", "/admin/game-settings",
                new[] { "SettingController", "GameController" }, None),

            // ── Transactions ────────────────────────────────────────────
            new PageDefinition("transactions-items", "Item Transactions", "Transactions", "/admin/transactions",
                new[] { "TransactionsController", "TransactionAuditLogController", "TransactionsReportsController", "ChannelController" }, None),
            new PageDefinition("transactions-game", "Game Transactions", "Transactions", "/admin/game-transactions",
                new[] { "GameSessionController", "TransactionsController" }, None),

            // ── Venue ───────────────────────────────────────────────────
            new PageDefinition("rooms", "Rooms", "Venue", "/admin/rooms", new[] { "RoomController" }, None),
            new PageDefinition("discounts", "Discount Management", "Venue", "/admin/discounts", new[] { "DiscountController" }, None),
            new PageDefinition("channels", "Channels", "Venue", "/admin/channels", new[] { "ChannelController" }, None),
            new PageDefinition("printers", "Printers", "Venue", "/admin/printers", new[] { "PrinterController", "PrintingController" }, None),

            // ── Events & website ────────────────────────────────────────
            new PageDefinition("events", "Manage Events", "Events & Website", "/admin/events",
                new[] { "EventsAdminController", "EventsTillController" }, None),
            new PageDefinition("event-registrations", "Event Registrations", "Events & Website", "/admin/event-registrations",
                new[] { "EventRegistrationsAdminController" }, None),
            new PageDefinition("website", "Website", "Events & Website", "/admin/website",
                new[] { "SiteContentAdminController" }, None),

            // ── Tools ───────────────────────────────────────────────────
            new PageDefinition("audit-logs", "Audit Logs", "Tools", "/admin/audit-logs",
                new[] { "AdminAuditController", "TransactionAuditLogController" }, None),
            new PageDefinition("ai-assistant", "AI Assistant", "Tools", "/ai/chat",
                new[] { "AiChatController", "PendingActionsController" }, None),
            new PageDefinition("integrations", "Integrations", "Tools", "/admin/integrations",
                new[] { "IntegrationSettingsController" }, None),
            new PageDefinition("cogs-rebuild", "COGS Rebuild", "Tools", "/admin/consumption-rebuild",
                new[] { "StockController" }, None),

            // ── Rewards ─────────────────────────────────────────────────
            new PageDefinition("loyalty-customers", "Customer Lookup", "AXIS PLUS Rewards", "/admin/loyalty/customers",
                new[] { "LoyaltyController", "UserCardController", "CardController" }, None),
            new PageDefinition("loyalty-leaderboard", "Leaderboard", "AXIS PLUS Rewards", "/admin/loyalty/leaderboard",
                new[] { "LoyaltyController" }, None),
            new PageDefinition("loyalty-draws", "Conduct Draws", "AXIS PLUS Rewards", "/admin/loyalty/draws",
                new[] { "LoyaltyController" }, None),
            new PageDefinition("wallets", "Wallets", "AXIS PLUS Rewards", "/admin/wallets",
                new[] { "WalletsController" }, None),
            new PageDefinition("online-payments", "Online Payments", "Accounting", "/admin/online-payments",
                new[] { "PaymentsController" }, None),
            new PageDefinition("online-orders", "Online Orders", "Till", "/cashier/online-orders",
                new[] { "ShopController" }, new[] { "cashier", "admin_fnb" }),
            new PageDefinition("shipments", "Shipping & Aramex", "Accounting", "/admin/shipments",
                new[] { "ShippingController" }, None),

            // ── Accounting ──────────────────────────────────────────────
            new PageDefinition("accounting", "Accounting Dashboard", "Accounting", "/accounting",
                new[] { "AccountingController", "JournalController", "AccountsController" }, None),
            new PageDefinition("accounting-item-revenue", "Item Revenue", "Accounting", "/accounting/item-revenue",
                new[] { "ItemRevenueReportController" }, None),
            new PageDefinition("owners-drawings", "Owners' Drawings", "Accounting", "/accounting/owners-drawings",
                new[] { "OwnersController", "AccountsController" }, None),
            new PageDefinition("accounting-accounts", "Chart of Accounts", "Accounting", "/accounting/accounts",
                new[] { "AccountsController" }, None),
            new PageDefinition("accounting-trial-balance", "Trial Balance", "Accounting", "/accounting/trial-balance",
                new[] { "AccountingController" }, None),
            new PageDefinition("accounting-ledger", "General Ledger", "Accounting", "/accounting/general-ledger",
                new[] { "AccountingController", "JournalController" }, None),
            new PageDefinition("accounting-audit", "Books Audit", "Accounting", "/accounting/audit",
                new[] { "AccountingController" }, None),
            new PageDefinition("accounting-hierarchy", "Hierarchy Audit", "Accounting", "/accounting/hierarchy-audit",
                new[] { "AccountingController", "AccountsController" }, None),

            // ── Entries ─────────────────────────────────────────────────
            new PageDefinition("expenses", "Entries", "Entries Management", "/admin/expenses",
                new[] { "ExpenseController", "ExpenseCategoryController" }, None),
            new PageDefinition("expense-categories", "Entry Categories", "Entries Management", "/admin/expense-categories",
                new[] { "ExpenseCategoryController" }, None),

            // ── Food & beverage till ────────────────────────────────────
            new PageDefinition("cashier-items", "Items (till)", "Till", "/cashier/items", FoodTill.Concat(new[] { "PromotionsAutoApplyController", "DisplaysController", "BarcodesController", "SerialsController" }).ToArray(), new[] { "cashier" }),
            new PageDefinition("cashier-orders", "Orders (till)", "Till", "/cashier/orders",
                new[] { "TransactionsController", "KitchenController" }, new[] { "cashier" }),
            new PageDefinition("open-invoices", "Open Items Invoice", "Till", "/cashier/open-invoices",
                new[] { "TransactionsController", "PromotionsAutoApplyController", "WhishPaymentsController", "DisplaysController", "TendersController", "CouponsController", "GiftCardsController" }, new[] { "cashier", "gamecashier" }),
            new PageDefinition("clients", "Clients", "Till", "/gamecashier/clients",
                new[] { "WalletsController", "LoyaltyController", "UsersController", "UserCardController" }, new[] { "cashier", "gamecashier", "admin_fnb" }),
            new PageDefinition("cashier-events", "Events Board", "Till", "/cashier/events",
                new[] { "EventsTillController" }, new[] { "cashier", "gamecashier", "admin_fnb" }),
            new PageDefinition("loyalty-check", "AXIS PLUS Check", "Till", "/cashier/loyalty-check",
                new[] { "LoyaltyController", "UserCardController" }, new[] { "cashier", "gamecashier" }),

            // ── Game till ───────────────────────────────────────────────
            new PageDefinition("game-sessions", "Game Session", "Game Till", "/game/sessions", GameTill, new[] { "gamecashier" }),
            new PageDefinition("ps5-sessions", "PS5 Sessions", "Game Till", "/gamecashier/ps5-sessions", GameTill, new[] { "gamecashier" }),
            new PageDefinition("board-sessions", "Board Games", "Game Till", "/gamecashier/board-sessions", GameTill, new[] { "gamecashier" }),
            new PageDefinition("gamecashier-items", "Items (game till)", "Game Till", "/gamecashier/items", GameTill, new[] { "gamecashier" }),
            new PageDefinition("gamecashier-rooms", "Rooms (game till)", "Game Till", "/gamecashier/rooms",
                new[] { "RoomController" }, new[] { "gamecashier" }),

            // ── F&B admin ───────────────────────────────────────────────
            new PageDefinition("fnb-dashboard", "F&B Dashboard", "F&B Admin", "/admin-fnb/dashboard",
                new[] { "ProfitController", "TransactionsReportsController", "TransactionsController" }, new[] { "admin_fnb" }),
            new PageDefinition("fnb-profit", "F&B Profit", "F&B Admin", "/admin-fnb/profit",
                new[] { "ProfitController", "TransactionsReportsController" }, new[] { "admin_fnb" }),
            new PageDefinition("fnb-items", "F&B Items", "F&B Admin", "/admin-fnb/items",
                new[] { "ItemController", "CategoryController" }, new[] { "admin_fnb" }),
            new PageDefinition("fnb-orders", "F&B Orders", "F&B Admin", "/admin-fnb/orders",
                new[] { "TransactionsController", "KitchenController" }, new[] { "admin_fnb" }),

            // ── Kitchen & bar ───────────────────────────────────────────
            new PageDefinition("kitchen-display", "Kitchen Orders", "Kitchen & Bar", "/chef/kitchen-display",
                new[] { "KitchenController", "KitchenBarOrderController" }, new[] { "chef" }),
            new PageDefinition("kitchen-stats", "Kitchen Stats", "Kitchen & Bar", "/chef/stats",
                new[] { "KitchenController" }, new[] { "chef" }),
            new PageDefinition("bar-display", "Bar Orders", "Kitchen & Bar", "/bartender/bar-display",
                new[] { "KitchenController", "KitchenBarOrderController" }, new[] { "bartender" }),

            // ── Stock operations ───────────────────────────────────────
            new PageDefinition("stockops-locations", "Stock Locations", "Stock Management", "/stock/locations", new[] { "StockLocationsController" }, Stock),
            new PageDefinition("stockops-counts", "Stocktakes", "Stock Management", "/stock/counts", new[] { "StockCountsController" }, Stock),
            new PageDefinition("stockops-transfers", "Stock Transfers", "Stock Management", "/stock/transfers", new[] { "StockTransfersController" }, Stock),
            new PageDefinition("stockops-reorder", "Reorder Suggestions", "Stock Management", "/stock/reorder", new[] { "ReorderController" }, Stock),
            new PageDefinition("stockops-batches", "Batches & Expiry", "Stock Management", "/stock/batches", new[] { "StockBatchesController" }, Stock),
            new PageDefinition("stockops-positions", "Branch Stock Positions", "Stock Management", "/stock/positions", new[] { "BranchStockController", "StockLocationsController", "StockTransfersController" }, Stock),
            new PageDefinition("retail-barcodes", "Barcodes", "Inventory Management", "/admin/retail/barcodes", new[] { "BarcodesController" }, None),
            new PageDefinition("retail-labels", "Barcode Labels", "Inventory Management", "/admin/retail/labels", new[] { "BarcodesController" }, None),
            new PageDefinition("retail-serials", "Serial Numbers", "Inventory Management", "/admin/retail/serials", new[] { "SerialsController" }, None),

            // ── Floor, tables & reservations ───────────────────────────
            new PageDefinition("floor-plan", "Floor Plan", "Venue", "/admin/floor-plan", new[] { "FloorController" }, None),
            new PageDefinition("floor-view", "Floor", "Till", "/cashier/floor", new[] { "FloorController", "TableSessionController" }, TillRoles),
            new PageDefinition("floor-reservations", "Reservations", "Venue", "/floor/reservations", new[] { "ReservationController", "FloorController" }, TillRoles),
            new PageDefinition("waiter", "Waiter", "Till", "/waiter", new[] { "FloorController", "TableSessionController", "ReservationController" }, TillRoles),

            // ── Till controls & currency ───────────────────────────────
            new PageDefinition("till-shift", "My Shift", "Till", "/till/shift", new[] { "ShiftsController", "CurrenciesController" }, TillRoles),
            new PageDefinition("till-void-requests", "Void / Refund Requests", "Till", "/till/void-requests", new[] { "VoidRequestsController", "VoidReasonsController" }, TillRoles),
            new PageDefinition("till-tenders", "Tenders (multi-currency)", "Till", "/till/tenders", new[] { "TendersController", "CurrenciesController" }, TillRoles),
            new PageDefinition("till-shifts-admin", "Cashier Shifts & Z Reports", "Transactions", "/admin/till/shifts", new[] { "ShiftsController", "CurrenciesController" }, None),
            new PageDefinition("till-void-reasons", "Void Reasons", "Transactions", "/admin/till/void-reasons", new[] { "VoidReasonsController" }, None),
            new PageDefinition("currencies", "Currencies & Rates", "Accounting", "/admin/currencies", new[] { "CurrenciesController" }, None),
            new PageDefinition("offline-status", "Offline Status", "Till", "/offline", new[] { "OfflineController" }, TillRoles),
            new PageDefinition("till-displays", "Customer Displays", "Till", "/admin/till/displays", new[] { "DisplaysController" }, None),
            new PageDefinition("whish-payments", "Whish Payments", "Till", "/cashier/open-invoices", new[] { "WhishPaymentsController", "DisplaysController" }, TillRoles),

            // ── Delivery, self-order & kiosk ───────────────────────────
            new PageDefinition("delivery-board", "Delivery & Takeaway", "Till", "/delivery/board", new[] { "DeliveryOrdersController", "DeliveryZonesController", "DeliveryDriversController" }, TillRoles),
            new PageDefinition("delivery-new", "New Delivery Order", "Till", "/delivery/new", new[] { "DeliveryOrdersController", "DeliveryZonesController" }, TillRoles),
            new PageDefinition("delivery-zones", "Zones & Drivers", "F&B Admin", "/admin/delivery/zones-drivers", new[] { "DeliveryZonesController", "DeliveryDriversController" }, None),
            new PageDefinition("selforder-tables", "Self-Order Tables (QR)", "F&B Admin", "/admin/selforder/tables", new[] { "SelfOrderTablesController" }, None),
            new PageDefinition("kiosk-devices", "Kiosk Devices", "F&B Admin", "/admin/kiosk/devices", new[] { "KioskDevicesController" }, None),

            // ── Accounting depth ───────────────────────────────────────
            new PageDefinition("acctplus-bank", "Bank Reconciliation", "Accounting", "/accounting-plus/bank-reconciliation", new[] { "BankReconciliationController" }, None),
            new PageDefinition("acctplus-bills", "Supplier Bills", "Accounting", "/accounting-plus/supplier-bills", new[] { "SupplierBillsController" }, None),
            new PageDefinition("acctplus-aging", "AP / AR Aging", "Accounting", "/accounting-plus/aging", new[] { "SupplierBillsController", "ReceivablesController" }, None),
            new PageDefinition("acctplus-vat", "VAT Report", "Accounting", "/accounting-plus/vat-report", new[] { "TaxController" }, None),
            new PageDefinition("acctplus-assets", "Fixed Assets", "Accounting", "/accounting-plus/fixed-assets", new[] { "FixedAssetsController" }, None),
            new PageDefinition("acctplus-periods", "Accounting Periods", "Accounting", "/accounting-plus/periods", new[] { "AccountingPeriodsController" }, None),
            new PageDefinition("menu-engineering", "Menu Engineering", "Accounting", "/accounting/menu-engineering", new[] { "MenuEngineeringController" }, new[] { "admin_fnb" }),

            // ── HR ─────────────────────────────────────────────────────
            new PageDefinition("hr-employees", "Employees", "People", "/hr/employees", new[] { "HrEmployeesController" }, None),
            new PageDefinition("hr-schedule", "Shift Schedule", "People", "/hr/schedule", new[] { "HrScheduleController" }, None),
            new PageDefinition("hr-attendance", "Attendance", "People", "/hr/attendance", new[] { "HrAttendanceController", "HrEmployeesController" }, None),
            new PageDefinition("hr-leave", "Leave", "People", "/hr/leave", new[] { "HrLeaveController", "HrEmployeesController" }, None),
            new PageDefinition("hr-tips", "Tip Pools", "People", "/hr/tips", new[] { "HrTipsController", "HrEmployeesController" }, TillRoles),
            new PageDefinition("hr-payroll", "Payroll", "People", "/hr/payroll", new[] { "HrPayrollController", "HrEmployeesController" }, None),
            new PageDefinition("hr-pay-components", "Pay Components", "People", "/hr/pay-components", new[] { "HrPayrollController", "HrEmployeesController" }, None),
            new PageDefinition("hr-clock", "Clock In / Out", "Till", "/hr/clock", new[] { "HrAttendanceController" }, new[] { "cashier", "gamecashier", "admin_fnb", "chef", "bartender" }),

            // ── Promotions ─────────────────────────────────────────────
            new PageDefinition("promo-giftcards", "Gift Cards", "Promotions", "/admin/promotions/gift-cards", new[] { "GiftCardsController" }, None),
            new PageDefinition("promo-coupons", "Coupons", "Promotions", "/admin/promotions/coupons", new[] { "CouponsController" }, None),
            new PageDefinition("promo-happyhours", "Happy Hours", "Promotions", "/admin/promotions/happy-hours", new[] { "HappyHourController" }, None),
            new PageDefinition("promo-combos", "Combos", "Promotions", "/admin/promotions/combos", new[] { "CombosController" }, None),
            new PageDefinition("promo-accounting", "Promotion Accounting", "Promotions", "/admin/promotions/accounting", new[] { "PromotionAccountingController" }, None),
            new PageDefinition("till-coupon-apply", "Apply Coupon", "Till", "/cashier/coupon", new[] { "CouponsController", "GiftCardsController", "HappyHourController", "CombosController", "PromotionsAutoApplyController" }, TillRoles),

            // ── Branches & BI ──────────────────────────────────────────
            new PageDefinition("branches", "Branches", "Venue", "/admin/branches", new[] { "BranchesController" }, None),
            new PageDefinition("branches-consolidated", "Consolidated Dashboard", "Overview", "/admin/branches/consolidated", new[] { "BranchesController" }, None),
            new PageDefinition("bi-scheduled-reports", "Scheduled Reports", "Tools", "/admin/bi/reports", new[] { "BiReportsController" }, None),
            new PageDefinition("bi-owner-view", "Owner View", "Overview", "/owner", new[] { "BiOwnerSummaryController" }, new[] { "admin_fnb" }),

            // ── Marketing ──────────────────────────────────────────────
            new PageDefinition("marketing-segments", "Segments", "Marketing", "/admin/marketing/segments", new[] { "MarketingSegmentsController" }, None),
            new PageDefinition("marketing-campaigns", "Campaigns", "Marketing", "/admin/marketing/campaigns", new[] { "MarketingCampaignsController", "MarketingSegmentsController", "MarketingUnsubscribesController" }, None),
        };

        /// <summary>Pages the social media manager role starts with.</summary>
        public static readonly string[] SocialMediaPages = { "events", "event-registrations", "website" };

        public static readonly HashSet<string> Keys = Pages.Select(p => p.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

        public static bool IsKnown(string key) => Keys.Contains(key);

        /// <summary>Page keys that grant access to the given controller type name.</summary>
        public static IReadOnlyList<string> KeysForController(string controllerTypeName) =>
            Pages.Where(p => p.Controllers.Contains(controllerTypeName, StringComparer.OrdinalIgnoreCase))
                 .Select(p => p.Key)
                 .ToList();

        /// <summary>Pages a built-in role had before permissions became editable.</summary>
        public static IReadOnlyList<string> LegacyPagesFor(string role) =>
            Pages.Where(p => p.LegacyRoles.Contains(role, StringComparer.OrdinalIgnoreCase))
                 .Select(p => p.Key)
                 .ToList();
    }
}
