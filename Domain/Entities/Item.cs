using System;
using System.Collections.Generic;

namespace Domain.Entities
{
    public class Item
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = default!;
        public int Quantity { get; set; }
        public decimal Price { get; set; }
        public string Type { get; set; } = default!; // BoardGame, CoffeeShop

        public Guid CategoryId { get; set; }
        public Category Category { get; set; } = default!;

        public Guid? GameId { get; set; }   // optional link to Game (if needed)
        public Game? Game { get; set; }

        public Guid StatusId { get; set; }
        public Status Status { get; set; } = default!;

        public ICollection<CoffeeShopOrder> CoffeeShopOrders { get; set; } = new List<CoffeeShopOrder>();

        public ICollection<TransactionItem> TransactionItems { get; set; } = new List<TransactionItem>();
    }
}
