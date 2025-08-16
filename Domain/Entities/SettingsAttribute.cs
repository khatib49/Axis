using System;

namespace Domain.Entities
{
    public class SettingsAttribute
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = default!;
        public string AttributeValue { get; set; } = default!;

        public Guid SettingsId { get; set; }
        public Setting Settings { get; set; } = default!;
    }
}
