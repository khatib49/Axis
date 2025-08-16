using System;
using System.Collections.Generic;

namespace Domain.Entities
{
    public class Category
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = default!;
        public ICollection<Item> Items { get; set; } = new List<Item>();
    }
}
