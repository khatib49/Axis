using System.Text.Json;

namespace Application.Services.Ai
{
    /// <summary>
    /// Static catalogue of tools we expose to Claude. Two flavours:
    ///
    ///   1. Read-only data tools — they SELECT from the DB and return JSON.
    ///      The most powerful is run_select_query which lets Claude write
    ///      arbitrary SELECT statements against a hardened query path.
    ///
    ///   2. Proposal tools — they create a PendingAiAction row and return
    ///      its id. They DO NOT execute; the admin must approve in the UI.
    ///
    /// Schemas use JSON Schema (draft 2020-12 compatible subset) as
    /// required by Anthropic's tool spec.
    /// </summary>
    public static class AiToolRegistry
    {
        public static List<ClaudeTool> All()
        {
            return new List<ClaudeTool>
            {
                // ── Read-only data tools ─────────────────────────────────
                Tool("get_revenue_summary",
                    "Returns total gross sales, discounts, and net revenue for a date range. " +
                    "Optionally filter by channel name (e.g. 'Toters', 'In-house').",
                    JsonDoc(@"{
                        ""type"": ""object"",
                        ""properties"": {
                            ""from"": { ""type"": ""string"", ""format"": ""date"", ""description"": ""Inclusive YYYY-MM-DD"" },
                            ""to"":   { ""type"": ""string"", ""format"": ""date"", ""description"": ""Inclusive YYYY-MM-DD"" },
                            ""channel"": { ""type"": ""string"", ""description"": ""Optional channel name"" }
                        },
                        ""required"": [""from"", ""to""]
                    }")),

                Tool("get_top_items",
                    "Top-selling menu items by quantity or revenue for a date range.",
                    JsonDoc(@"{
                        ""type"": ""object"",
                        ""properties"": {
                            ""from"":    { ""type"": ""string"", ""format"": ""date"" },
                            ""to"":      { ""type"": ""string"", ""format"": ""date"" },
                            ""by"":      { ""type"": ""string"", ""enum"": [""revenue"", ""quantity""], ""default"": ""revenue"" },
                            ""limit"":   { ""type"": ""integer"", ""default"": 10 }
                        },
                        ""required"": [""from"", ""to""]
                    }")),

                Tool("get_low_stock",
                    "List ingredients at or below their reorder level. No arguments.",
                    JsonDoc(@"{ ""type"": ""object"", ""properties"": {} }")),

                Tool("get_occupancy_now",
                    "Current lounge occupancy: rooms in use vs total active rooms, % busy, " +
                    "and the most-played game in the last 14 days.",
                    JsonDoc(@"{ ""type"": ""object"", ""properties"": {} }")),

                Tool("get_recent_players",
                    "Recent customers who played a given game (or any game), with their phone " +
                    "numbers and last-visit date. Use this to target tournament invites.",
                    JsonDoc(@"{
                        ""type"": ""object"",
                        ""properties"": {
                            ""game"":      { ""type"": ""string"", ""description"": ""Optional game name; null = any"" },
                            ""within_days"": { ""type"": ""integer"", ""default"": 30 },
                            ""limit"":     { ""type"": ""integer"", ""default"": 30 }
                        }
                    }")),

                Tool("get_due_regulars",
                    "Customers whose typical visit-day matches today/tomorrow but who haven't " +
                    "shown up in their usual window. Returns name + phone + usual time slot.",
                    JsonDoc(@"{
                        ""type"": ""object"",
                        ""properties"": {
                            ""within_hours"": { ""type"": ""integer"", ""default"": 24 },
                            ""limit"":        { ""type"": ""integer"", ""default"": 20 }
                        }
                    }")),

                Tool("get_expense_summary",
                    "Sum of expenses grouped by category for a date range.",
                    JsonDoc(@"{
                        ""type"": ""object"",
                        ""properties"": {
                            ""from"": { ""type"": ""string"", ""format"": ""date"" },
                            ""to"":   { ""type"": ""string"", ""format"": ""date"" }
                        },
                        ""required"": [""from"", ""to""]
                    }")),

                Tool("run_select_query",
                    "Run an arbitrary read-only SELECT statement against the database when " +
                    "the other tools don't fit. ONLY SELECT is allowed; INSERT/UPDATE/DELETE/DDL " +
                    "are rejected. Limit yourself to <= 200 rows in the result. Tables you can " +
                    "query include: Items, Categories, Channels, Rooms, TransactionRecords, " +
                    "TransactionItems, JournalEntries, JournalEntryLines, Accounts, AccountTypes, " +
                    "Expenses, ExpenseCategories, Ingredients, RecipeLines, StockMovements, " +
                    "Suppliers, Purchases, PurchaseLines, LoyaltyCustomers, LoyaltyTickets, " +
                    "AdminAuditLogs, TransactionAuditLogs.",
                    JsonDoc(@"{
                        ""type"": ""object"",
                        ""properties"": {
                            ""sql"": { ""type"": ""string"", ""description"": ""SELECT statement (no semicolons after, no CTEs that write)"" }
                        },
                        ""required"": [""sql""]
                    }")),

                // ── Proposal tools (require admin approval) ─────────────
                Tool("propose_flash_tournament",
                    "PROPOSE a flash tournament for admin approval. Does NOT execute. The admin " +
                    "will review and either approve (sending WhatsApp invites to the recipient " +
                    "list) or reject.",
                    JsonDoc(@"{
                        ""type"": ""object"",
                        ""properties"": {
                            ""game"":            { ""type"": ""string"", ""description"": ""e.g. Tekken, FIFA, Mortal Kombat"" },
                            ""start_at_iso"":    { ""type"": ""string"", ""format"": ""date-time"", ""description"": ""When the tournament starts (ISO 8601)"" },
                            ""entry_fee"":       { ""type"": ""number"" },
                            ""prize_pool"":      { ""type"": ""number"" },
                            ""recipient_phones"": {
                                ""type"": ""array"",
                                ""items"": { ""type"": ""string"" },
                                ""description"": ""E.164 phone numbers to invite""
                            },
                            ""recipient_names"": {
                                ""type"": ""array"",
                                ""items"": { ""type"": ""string"" }
                            },
                            ""hype_message"": {
                                ""type"": ""string"",
                                ""description"": ""Personalised body. Use {{name}} as placeholder for the recipient's name.""
                            },
                            ""reason"": {
                                ""type"": ""string"",
                                ""description"": ""One-line rationale for the admin (e.g. 'Occupancy at 30%, Tekken is top game last 14d')""
                            }
                        },
                        ""required"": [""game"", ""start_at_iso"", ""entry_fee"", ""prize_pool"", ""recipient_phones"", ""hype_message""]
                    }")),

                Tool("propose_customer_ping",
                    "PROPOSE a 'we saved your spot' personalised ping to a single customer or " +
                    "small list. For one-to-few re-engagement. Does NOT execute.",
                    JsonDoc(@"{
                        ""type"": ""object"",
                        ""properties"": {
                            ""recipients"": {
                                ""type"": ""array"",
                                ""items"": {
                                    ""type"": ""object"",
                                    ""properties"": {
                                        ""phone"": { ""type"": ""string"" },
                                        ""name"":  { ""type"": ""string"" },
                                        ""message"": { ""type"": ""string"" }
                                    },
                                    ""required"": [""phone"", ""message""]
                                }
                            },
                            ""reason"": { ""type"": ""string"" }
                        },
                        ""required"": [""recipients""]
                    }")),
            };
        }

        // ── helpers ──────────────────────────────────────────────────────
        private static ClaudeTool Tool(string name, string description, JsonElement schema) =>
            new() { Name = name, Description = description, InputSchema = schema };

        private static JsonElement JsonDoc(string json) =>
            JsonDocument.Parse(json).RootElement.Clone();
    }
}
