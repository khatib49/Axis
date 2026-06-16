using System;
using System.Collections.Generic;

namespace Domain.Entities
{
    public class Item
    {
        public int Id { get; set; }
        public string Name { get; set; } = default!;
        public int Quantity { get; set; }
        public decimal Price { get; set; }
        public string Type { get; set; } = default!; // BoardGame, CoffeeShop

        public int CategoryId { get; set; }
        public Category Category { get; set; } = default!;

        public int? GameId { get; set; }   // optional link to Game (if needed)
        public Game? Game { get; set; }

        public int StatusId { get; set; }
        public Status Status { get; set; } = default!;
        public string? ImagePath { get; set; }
        public decimal? BuyPrice { get; set; }

        public ICollection<CoffeeShopOrder> CoffeeShopOrders { get; set; } = new List<CoffeeShopOrder>();

        public ICollection<TransactionItem> TransactionItems { get; set; } = new List<TransactionItem>();

        // Recipe lines — empty for items that don't track stock yet
        // (drinks, packaged goods at rollout, anything not configured by
        // the chef). Items with no recipe sell normally and don't deduct
        // anything, per the rollout decision.
        public ICollection<RecipeLine> RecipeLines { get; set; } = new List<RecipeLine>();
    }
}
