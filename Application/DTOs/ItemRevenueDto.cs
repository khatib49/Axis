namespace Application.DTOs
{
    public class ItemRevenueReportRequestDto
    {
        public DateTime? From { get; set; }
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
        // Buy price (current, nullable)
        public decimal? BuyPrice { get; set; }

        // Sales in period
        public int UnitsSold { get; set; }
        public decimal Revenue { get; set; }          // discount-aware proportional revenue
        public decimal Cogs { get; set; }             // BuyPrice × UnitsSold
        public decimal GrossProfit { get; set; }      // Revenue - Cogs
        public decimal? GrossMarginPct { get; set; }  // GrossProfit / Revenue × 100

        // Current stock
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
        public List<ItemRevenueLineDto> Items { get; set; } = new();

        // Subtotals
        public int TotalUnitsSold { get; set; }
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
        public List<int> FilteredCategoryIds { get; set; } = new();

        public List<ItemRevenueCategoryGroupDto> Categories { get; set; } = new();

        // Grand totals
        public int GrandTotalUnitsSold { get; set; }
        public decimal GrandTotalRevenue { get; set; }
        public decimal GrandTotalCogs { get; set; }
        public decimal GrandTotalGrossProfit { get; set; }
        public decimal? GrandGrossMarginPct { get; set; }
        public decimal GrandTotalStockBuyValue { get; set; }
        public decimal GrandTotalStockSellValue { get; set; }
        public decimal GrandTotalStockPotentialProfit { get; set; }

        // TCG summary (TCG + TCG Retail combined)
        public decimal TcgRevenue { get; set; }
        public decimal TcgCogs { get; set; }
        public decimal TcgGrossProfit { get; set; }
        public decimal TcgStockBuyValue { get; set; }
        public decimal TcgStockSellValue { get; set; }
    }

}
