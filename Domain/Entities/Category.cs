using System;
using System.Collections.Generic;

namespace Domain.Entities
{
    public class Category
    {
        public int Id { get; set; }
        public string Name { get; set; } = default!;
        public string Type { get; set; } = default!; // e.g., "Action", "Adventure", "RPG", etc.
        public ICollection<Item> Items { get; set; } = new List<Item>();
    }
}
