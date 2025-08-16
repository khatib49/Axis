using System;
using System.Collections.Generic;

namespace Domain.Entities
{
    public class Card
    {
        public Guid Id { get; set; }
        public string CardName { get; set; } = default!;
        public string CardType { get; set; } = default!;
        public bool IsActive { get; set; } = true;
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        public DateTime? ModifiedOn { get; set; }

        public ICollection<UserCard> UserCards { get; set; } = new List<UserCard>();
        public ICollection<GameSession> GameSessions { get; set; } = new List<GameSession>();
        public ICollection<TransactionRecord> Transactions { get; set; } = new List<TransactionRecord>();
        public ICollection<Receipt> Receipts { get; set; } = new List<Receipt>();
        public ICollection<CoffeeShopOrder> CoffeeShopOrders { get; set; } = new List<CoffeeShopOrder>();
    }
}
