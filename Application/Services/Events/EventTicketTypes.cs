using System.Security.Cryptography;
using System.Text.Json;
using Application.DTOs;

namespace Application.Services.Events
{
    /// <summary>
    /// Ticket types of an event (Standard $15, VIP $25 …), stored as JSON on
    /// Events.TicketTypesJson. Pure helpers so the admin save path, the public
    /// page and registration all read and validate them the same way.
    /// </summary>
    public static class EventTicketTypes
    {
        public const int MaxTypes = 12;
        public const int MaxNameLength = 80;
        public const int MaxDescriptionLength = 200;

        private static readonly JsonSerializerOptions Json = new()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        public static List<EventTicketTypeDto> Parse(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new();
            try
            {
                return (JsonSerializer.Deserialize<List<EventTicketTypeDto>>(json, Json) ?? new())
                    .Where(t => !string.IsNullOrWhiteSpace(t.Key) && !string.IsNullOrWhiteSpace(t.Name))
                    .ToList();
            }
            catch (JsonException) { return new(); }
        }

        public static string? Serialize(IReadOnlyCollection<EventTicketTypeDto> types) =>
            types.Count == 0 ? null : JsonSerializer.Serialize(types, Json);

        public static List<EventTicketTypeDto> Active(string? json) =>
            Parse(json).Where(t => t.IsActive).ToList();

        /// <summary>
        /// Cleans what the admin sent: trims, rounds prices to cents, drops empty
        /// capacities, keeps existing keys and mints keys for new rows.
        /// <paramref name="keysInUse"/> are types that already have registrations:
        /// if the admin removed one, it comes back hidden instead of disappearing,
        /// so sales reports and the door list keep their label.
        /// </summary>
        public static List<EventTicketTypeDto> Normalize(
            IEnumerable<EventTicketTypeDto> input,
            IReadOnlyCollection<EventTicketTypeDto> existing,
            IReadOnlySet<string> keysInUse)
        {
            var result = new List<EventTicketTypeDto>();
            var seenKeys = new HashSet<string>(StringComparer.Ordinal);
            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var existingKeys = existing.Select(e => e.Key!).ToHashSet(StringComparer.Ordinal);

            foreach (var raw in input)
            {
                var name = (raw.Name ?? "").Trim();
                if (name.Length == 0) throw new ArgumentException("Every ticket type needs a name.");
                if (name.Length > MaxNameLength) throw new ArgumentException($"Ticket type names are limited to {MaxNameLength} characters.");
                if (!seenNames.Add(name)) throw new ArgumentException($"Two ticket types are both called \"{name}\". Give each a different name.");
                if (raw.Price < 0) throw new ArgumentException($"\"{name}\" has a negative price.");

                // Keep a key the server issued; anything else (new row, or a key
                // invented by the client) gets a fresh one.
                var key = raw.Key is { Length: > 0 } k && existingKeys.Contains(k) && !seenKeys.Contains(k) ? k : NewKey(seenKeys);
                seenKeys.Add(key);

                var desc = string.IsNullOrWhiteSpace(raw.Description) ? null : raw.Description.Trim();
                if (desc is { Length: > MaxDescriptionLength }) desc = desc[..MaxDescriptionLength];

                result.Add(new EventTicketTypeDto(
                    key, name, Math.Round(raw.Price, 2), desc,
                    raw.Capacity is > 0 ? raw.Capacity : null,
                    raw.IsActive));
            }

            foreach (var old in existing)
                if (keysInUse.Contains(old.Key!) && !seenKeys.Contains(old.Key!))
                {
                    result.Add(old with { IsActive = false });
                    seenKeys.Add(old.Key!);
                }

            if (result.Count > MaxTypes) throw new ArgumentException($"An event can have at most {MaxTypes} ticket types.");
            return result;
        }

        private static string NewKey(HashSet<string> taken)
        {
            const string alphabet = "abcdefghjkmnpqrstuvwxyz23456789";
            while (true)
            {
                var bytes = RandomNumberGenerator.GetBytes(8);
                var key = "tt_" + new string(bytes.Select(b => alphabet[b % alphabet.Length]).ToArray());
                if (!taken.Contains(key)) return key;
            }
        }
    }
}
