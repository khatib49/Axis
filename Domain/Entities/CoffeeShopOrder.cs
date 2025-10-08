using System;
using Domain.Identity;

namespace Domain.Entities
{
    public class CoffeeShopOrder
    {
        public int Id { get; set; }

        public int UserId { get; set; }
        public AppUser User { get; set; } = default!;

        public int CardId { get; set; }
        public Card Card { get; set; } = default!;

        public int ItemId { get; set; }
        public Item Item { get; set; } = default!;

        public int Quantity { get; set; }
        public decimal Price { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}
