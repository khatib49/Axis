using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Application.DTOs;
using Application.IServices;

namespace Application.Services.Offline
{
    /// <summary>
    /// Builds the compact bundle the till caches for an internet cut. Reuses
    /// the existing list services (large page size) so pricing rules stay in
    /// one place. Images travel as path strings only.
    /// </summary>
    public class OfflineSnapshotService : IOfflineSnapshotService
    {
        private const int PageSize = 5000;
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        private readonly ICategoryService _categories;
        private readonly IItemService _items;
        private readonly IDiscountService _discounts;
        private readonly IChannelService _channels;

        public OfflineSnapshotService(ICategoryService categories, IItemService items, IDiscountService discounts, IChannelService channels)
        {
            _categories = categories;
            _items = items;
            _discounts = discounts;
            _channels = channels;
        }

        public async Task<OfflineSnapshotDto> BuildAsync(CancellationToken ct = default)
        {
            var categories = (await _categories.ListAsync(new BasePaginationRequestDto(1, PageSize), ct)).Data;
            // StatusId null => enabled items only (same default the cashier pages use).
            var items = (await _items.ListAsync(new BasePaginationRequestDto(1, PageSize), ct)).Data;
            var discounts = (await _discounts.ListAsync(new BasePaginationRequestDto(1, PageSize), ct)).Data
                .Where(d => d.IsActive).ToList();
            var channels = (await _channels.ListAsync(includeHidden: false, ct)).Where(c => c.IsActive).ToList();

            var payload = new { categories, items, discounts, channels };
            var version = ComputeVersion(JsonSerializer.Serialize(payload, Json));

            return new OfflineSnapshotDto(version, DateTime.UtcNow, categories, items, discounts, channels);
        }

        private static string ComputeVersion(string json)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
            return Convert.ToHexString(bytes, 0, 8).ToLowerInvariant();
        }
    }
}
