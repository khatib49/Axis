using System;
using System.Collections.Generic;

namespace Domain.Entities
{
    public class Status
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = default!;
    }
}
