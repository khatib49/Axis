namespace Domain.Entities
{
    public class Category
    {
        public int Id { get; set; }
        public string Name { get; set; } = default!;
        public string Type { get; set; } = default!; // e.g., "Action", "Adventure", "RPG", etc.
        public string? ItemType { get; set; }
        public ICollection<Item> Items { get; set; } = new List<Item>();
    }
}
