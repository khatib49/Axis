namespace Application.DTOs
{
    public class ItemRevenueReportRequestDto
    {
        /// <summary>Inclusive lower bound — an instant (UTC). Null = no lower bound.</summary>
        public DateTime? From { get; set; }
        /// <summary>Exclusive upper bound — an instant (UTC). Null = no upper bound.</summary>
        public DateTime? To { get; set; }
        public List<int>? CategoryIds { get; set; }  // null = all item categories
    }

    // One row per item
    public class ItemRevenueLineDto
    {
        public int ItemId { get; set; }
        public string ItemName { get; set; } = string.Empty;
        public int CategoryId { get; set; }
        public string CategoryName { get; set; } = string.Empty;
        public string? ImagePath { get; set; }

        // Sell price (current)
        public decimal SellPrice { get; set; }
        // Buy price (current, nullable). Recipe items carry their cost in UnitCost instead.
        public decimal? BuyPrice { get; set; }

        /// <summary>Cost per unit actually used for COGS: BuyPrice, or the recipe's ingredient cost.</summary>
        public decimal? UnitCost { get; set; }
        /// <summary>"buy" (BuyPrice), "recipe" (ingredients × current cost), or "none" (no cost known → COGS 0).</summary>
        public string CostSource { get; set; } = "none";
        public bool IsRecipe { get; set; }
        /// <summary>Item is not Enabled (Disabled/Deleted) — kept only because it sold in the period; stock counted as 0.</summary>
        public bool IsDeleted { get; set; }

        // Sales in period
        public int UnitsSold { get; set; }
        /// <summary>Units handed out at $0 inside an event kit (stock left, no revenue, COGS counted).</summary>
        public int UnitsGivenFree { get; set; }
        public decimal GrossRevenue { get; set; }     // list value before discount (incl. add-ons)
        public decimal DiscountGiven { get; set; }    // Gross − Revenue
        public decimal AddOnRevenue { get; set; }     // net add-on money included in Revenue
        public decimal Revenue { get; set; }          // NET — what the customer actually paid for this item
        public decimal Cogs { get; set; }             // UnitCost × (UnitsSold + UnitsGivenFree)
        public decimal GrossProfit { get; set; }      // Revenue - Cogs
        public decimal? GrossMarginPct { get; set; }  // GrossProfit / Revenue × 100

        // Current stock (0 / "—" for recipe items — their stock lives on ingredients)
        public int StockOnHand { get; set; }
        public decimal StockBuyValue { get; set; }    // StockOnHand × BuyPrice
        public decimal StockSellValue { get; set; }   // StockOnHand × SellPrice
        public decimal StockPotentialProfit { get; set; } // StockSellValue - StockBuyValue
    }

    // Category subtotal row
    public class ItemRevenueCategoryGroupDto
    {
        public int CategoryId { get; set; }
        public string CategoryName { get; set; } = string.Empty;
        public string? ItemType { get; set; }
        /// <summary>TCG / retail shelf goods (Category.ItemType = Retail, or name contains "tcg").</summary>
        public bool IsTcg { get; set; }
        public List<ItemRevenueLineDto> Items { get; set; } = new();

        // Subtotals
        public int TotalUnitsSold { get; set; }
        public int TotalUnitsGivenFree { get; set; }
        public decimal TotalGrossRevenue { get; set; }
        public decimal TotalDiscount { get; set; }
        public decimal TotalAddOnRevenue { get; set; }
        public decimal TotalRevenue { get; set; }
        public decimal TotalCogs { get; set; }
        public decimal TotalGrossProfit { get; set; }
        public decimal? GrossMarginPct { get; set; }
        public decimal TotalStockBuyValue { get; set; }
        public decimal TotalStockSellValue { get; set; }
        public decimal TotalStockPotentialProfit { get; set; }
    }

    // Full report response
    public class ItemRevenueReportDto
    {
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
        /// <summary>Server time the numbers were computed — the "as of" stamp for the live view.</summary>
        public DateTime GeneratedAt { get; set; }
        /// <summary>Paid transactions in the period that carried at least one item line.</summary>
        public int TransactionCount { get; set; }
        public List<int> FilteredCategoryIds { get; set; } = new();

        public List<ItemRevenueCategoryGroupDto> Categories { get; set; } = new();

        // Grand totals
        public int GrandTotalUnitsSold { get; set; }
        public int GrandTotalUnitsGivenFree { get; set; }
        public decimal GrandTotalGrossRevenue { get; set; }
        public decimal GrandTotalDiscount { get; set; }
        public decimal GrandTotalAddOnRevenue { get; set; }
        public decimal GrandTotalRevenue { get; set; }
        public decimal GrandTotalCogs { get; set; }
        public decimal GrandTotalGrossProfit { get; set; }
        public decimal? GrandGrossMarginPct { get; set; }
        public decimal GrandTotalStockBuyValue { get; set; }
        public decimal GrandTotalStockSellValue { get; set; }
        public decimal GrandTotalStockPotentialProfit { get; set; }

        // TCG / retail summary
        public int TcgUnitsSold { get; set; }
        public decimal TcgRevenue { get; set; }
        public decimal TcgCogs { get; set; }
        public decimal TcgGrossProfit { get; set; }
        public decimal? TcgMarginPct { get; set; }
        public decimal TcgStockBuyValue { get; set; }
        public decimal TcgStockSellValue { get; set; }

        // F&B summary (everything that is not TCG / retail)
        public int FnbUnitsSold { get; set; }
        public decimal FnbRevenue { get; set; }
        public decimal FnbCogs { get; set; }
        public decimal FnbGrossProfit { get; set; }
        public decimal? FnbMarginPct { get; set; }
    }

}
