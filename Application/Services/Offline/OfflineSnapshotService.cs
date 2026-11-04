using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Infrastructure.IRepositories;
using Microsoft.EntityFrameworkCore;

namespace Application.Services.Offline
{
    /// <summary>
    /// Builds the compact bundle the till caches for an internet cut. Reuses
    /// the existing list services (large page size) so pricing rules stay in
    /// one place. Images travel as path strings only. Since phase 3 the
    /// bundle also carries what the game till needs to start a session and
    /// estimate its price offline: games, rooms with their sets (and the
    /// set status), the active game settings and the pass types.
    /// </summary>
    public class OfflineSnapshotService : IOfflineSnapshotService
    {
        private const int PageSize = 5000;
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        private readonly ICategoryService _categories;
        private readonly IItemService _items;
        private readonly IDiscountService _discounts;
        private readonly IChannelService _channels;
        private readonly IGameService _games;
        private readonly IRoomService _rooms;
        private readonly ISettingService _settings;
        private readonly IPassTypeService _passTypes;
        private readonly IBaseRepository<Set> _sets;

        public OfflineSnapshotService(
            ICategoryService categories,
            IItemService items,
            IDiscountService discounts,
            IChannelService channels,
            IGameService games,
            IRoomService rooms,
            ISettingService settings,
            IPassTypeService passTypes,
            IBaseRepository<Set> sets)
        {
            _categories = categories;
            _items = items;
            _discounts = discounts;
            _channels = channels;
            _games = games;
            _rooms = rooms;
            _settings = settings;
            _passTypes = passTypes;
            _sets = sets;
        }

        public async Task<OfflineSnapshotDto> BuildAsync(CancellationToken ct = default)
        {
            var page = new BasePaginationRequestDto(1, PageSize);

            var categories = (await _categories.ListAsync(page, ct)).Data;
            // StatusId null => enabled items only (same default the cashier pages use).
            var items = (await _items.ListAsync(page, ct)).Data;
            var discounts = (await _discounts.ListAsync(page, ct)).Data
                .Where(d => d.IsActive).ToList();
            var channels = (await _channels.ListAsync(includeHidden: false, ct)).Where(c => c.IsActive).ToList();

            // Game till: games (status travels so the till can grey out disabled
            // ones), the ACTIVE settings only (the default ListAsync overload hides
            // soft-deleted ones, exactly like the cashier page), pass types.
            var games = (await _games.ListAsync(page, ct)).Data;
            var gameSettings = (await _settings.ListAsync(page, ct)).Data;
            var passTypes = (await _passTypes.ListAsync(page, ct)).Data;
            var rooms = await BuildRoomsAsync(page, ct);

            var payload = new { categories, items, discounts, channels, games, rooms, gameSettings, passTypes };
            var version = ComputeVersion(JsonSerializer.Serialize(payload, Json));

            return new OfflineSnapshotDto(version, DateTime.UtcNow, categories, items, discounts, channels,
                games, rooms, gameSettings, passTypes);
        }

        /// <summary>Rooms from the room service, each with its sets and their status read straight from the Sets table.</summary>
        private async Task<List<OfflineRoomDto>> BuildRoomsAsync(BasePaginationRequestDto page, CancellationToken ct)
        {
            var rooms = (await _rooms.ListAsync(page, ct)).Data;

            var sets = await _sets.Query(asNoTracking: true)
                .Include(s => s.Status)
                .OrderBy(s => s.RoomId).ThenBy(s => s.Name)
                .Select(s => new { s.Id, s.RoomId, s.Name, s.StatusId, StatusName = s.Status != null ? s.Status.Name : null })
                .ToListAsync(ct);
            var setsByRoom = sets.GroupBy(s => s.RoomId)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<OfflineRoomSetDto>)g.Select(s => new OfflineRoomSetDto(s.Id, s.Name, s.StatusId, s.StatusName)).ToList());

            return rooms
                .Select(r => new OfflineRoomDto(r.Id, r.Name, r.CategoryId, r.CategoryName, r.IsOpenSet,
                    setsByRoom.TryGetValue(r.Id, out var list) ? list : Array.Empty<OfflineRoomSetDto>()))
                .ToList();
        }

        private static string ComputeVersion(string json)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
            return Convert.ToHexString(bytes, 0, 8).ToLowerInvariant();
        }
    }
}
