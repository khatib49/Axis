using System;
using System.Collections.Generic;

namespace Domain.Entities
{
    public class Setting
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = default!;
        public string Type { get; set; } = default!;

        public Guid GameId { get; set; }
        public Game Game { get; set; } = default!;

        public ICollection<SettingsAttribute> Attributes { get; set; } = new List<SettingsAttribute>();
        public ICollection<SettingsValue> Values { get; set; } = new List<SettingsValue>();
    }
}
