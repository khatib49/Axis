namespace Domain.Entities
{
    public class Category
    {
        public int Id { get; set; }
        public string Name { get; set; } = default!;
        public string Type { get; set; } = default!; // e.g., "Action", "Adventure", "RPG", etc.
        public string? ItemType { get; set; }
        public ICollection<Item> Items { get; set; } = new List<Item>();
        public int? AccountId { get; set; }
        public Account? Account { get; set; }

        /// <summary>Category is listed on the public online shop page (retail).</summary>
        public bool ShowInShop { get; set; }
        /// <summary>Weight used for shipping when an item has none (kg).</summary>
        public decimal? DefaultWeightKg { get; set; }

    }
}
