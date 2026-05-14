namespace Application.DTOs
{
    public class ItemSalesReportDto
    {
        public int ItemId { get; set; }
        public string ItemName { get; set; } = string.Empty;

        public int CategoryId { get; set; }
        public string CategoryName { get; set; } = string.Empty;

        public string ItemType { get; set; } = string.Empty; // CoffeeShop, BoardGame, etc.

        public int TotalQuantity { get; set; }           // how many units sold
        public decimal TotalAmount { get; set; }         // total revenue from this item

        public int OrdersCount { get; set; }             // in how many transactions it appeared
        public decimal AveragePerOrder { get; set; }     // TotalQuantity / OrdersCount
    }


    public class GameHourlySalesDto
    {
        public int Hour { get; set; }              // 0..23
        public int SessionsCount { get; set; }     // number of game transactions in that hour
        public decimal TotalHours { get; set; }    // sum of Hours for those sessions
        public decimal TotalAmount { get; set; }   // sum of TotalPrice for those sessions
        public decimal AverageSessionHours { get; set; }
        public decimal AverageSessionAmount { get; set; }
    }


}
