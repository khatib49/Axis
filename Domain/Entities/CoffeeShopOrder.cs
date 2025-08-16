using System;
using Domain.Identity;

namespace Domain.Entities
{
    public class CoffeeShopOrder
    {
        public Guid Id { get; set; }

        public Guid UserId { get; set; }
        public AppUser User { get; set; } = default!;

        public Guid CardId { get; set; }
        public Card Card { get; set; } = default!;

        public Guid ItemId { get; set; }
        public Item Item { get; set; } = default!;

        public int Quantity { get; set; }
        public decimal Price { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}
