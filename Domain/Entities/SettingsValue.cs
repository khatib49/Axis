using System;

namespace Domain.Entities
{
    public class SettingsValue
    {
        public Guid Id { get; set; }

        public Guid SettingsId { get; set; }
        public Setting Settings { get; set; } = default!;

        public Guid AttributeId { get; set; }
        public SettingsAttribute Attribute { get; set; } = default!;

        public string Value { get; set; } = default!;
    }
}
