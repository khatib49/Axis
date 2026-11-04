using Application.DTOs;
using Application.IServices;
using Application.Services.Offline;
using Domain.Entities;
using FluentAssertions;
using Infrastructure.Persistence;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using Xunit;

namespace Tests.Offline
{
    /// <summary>
    /// The offline snapshot carries what the game till needs to start and
    /// price a session without the server: games, rooms with their sets and
    /// set status, the active game settings and the pass types.
    /// </summary>
    public class OfflineSnapshotTests : IDisposable
    {
        private readonly ApplicationDbContext _db;
        private readonly Mock<ICategoryService> _categories = new();
        private readonly Mock<IItemService> _items = new();
        private readonly Mock<IDiscountService> _discounts = new();
        private readonly Mock<IChannelService> _channels = new();
        private readonly Mock<IGameService> _games = new();
        private readonly Mock<IRoomService> _rooms = new();
        private readonly Mock<ISettingService> _settings = new();
        private readonly Mock<IPassTypeService> _passTypes = new();

        public OfflineSnapshotTests()
        {
            _db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options);

            _categories.Setup(s => s.ListAsync(It.IsAny<BasePaginationRequestDto>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Page(new List<CategoryDto> { new(4, "PS5", "game", null) }));
            _items.Setup(s => s.ListAsync(It.IsAny<BasePaginationRequestDto>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Page(new List<ItemDto>()));
            _discounts.Setup(s => s.ListAsync(It.IsAny<BasePaginationRequestDto>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Page(new List<DiscountDto>()));
            _channels.Setup(s => s.ListAsync(false, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<ChannelDto>());

            _games.Setup(s => s.ListAsync(It.IsAny<BasePaginationRequestDto>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Page(new List<GameDto>
                {
                    new(1, "PS5 Station", 4, "PS5", 1, "Enabled", null, null),
                    new(2, "Catan", 5, "Board Games", 2, "Disabled", null, null),
                }));
            _rooms.Setup(s => s.ListAsync(It.IsAny<BasePaginationRequestDto>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Page(new List<RoomDto>
                {
                    new(1, "PS5 Room", 4, "PS5", 2, false),
                    new(2, "Board Hall", 5, "Board Games", 0, true),
                }));
            // The default (single-argument) overload is the one that hides soft-deleted settings.
            _settings.Setup(s => s.ListAsync(It.IsAny<BasePaginationRequestDto>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Page(new List<SettingDto>
                {
                    Setting(1, "PS5 – 1 hour", 1, 1m, 5m),
                    Setting(3, "PS5 – open hour", 1, 1m, 5m, isOpenHour: true),
                }));
            _settings.Setup(s => s.ListAsync(It.IsAny<BasePaginationRequestDto>(), true, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Page(new List<SettingDto>
                {
                    Setting(1, "PS5 – 1 hour", 1, 1m, 5m),
                    Setting(3, "PS5 – open hour", 1, 1m, 5m, isOpenHour: true),
                    Setting(9, "Old promo", 1, 1m, 1m, isActive: false),
                }));
            _passTypes.Setup(s => s.ListAsync(It.IsAny<BasePaginationRequestDto>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Page(new List<PassTypeDto> { new(1, "Day pass", 600, 12m, 2) }));

            _db.Set<Status>().AddRange(new Status { Id = 9, Name = "Free" }, new Status { Id = 10, Name = "Occupied" });
            _db.Set<Set>().AddRange(
                new Set { Id = 1, RoomId = 1, Name = "Station 1", StatusId = 10 },
                new Set { Id = 2, RoomId = 1, Name = "Station 2", StatusId = 9 },
                new Set { Id = 3, RoomId = 1, Name = "Station 3", StatusId = null });
            _db.SaveChanges();
        }

        public void Dispose() => _db.Dispose();

        private static PaginatedResponse<T> Page<T>(List<T> data) => new(data.Count, data, 1, 5000);

        private static SettingDto Setting(int id, string name, int gameId, decimal hours, decimal price, bool isOpenHour = false, bool isActive = true)
            => new(id, name, "Hourly", gameId, "PS5 Station", hours, price, DateTime.UtcNow, null, "seed", null,
                IsOffer: false, IsOpenHour: isOpenHour, IsDayPass: false, IsActive: isActive);

        private OfflineSnapshotService Build() => new(
            _categories.Object, _items.Object, _discounts.Object, _channels.Object,
            _games.Object, _rooms.Object, _settings.Object, _passTypes.Object,
            new BaseRepository<Set>(_db));

        [Fact]
        public async Task Snapshot_contains_games_rooms_with_sets_and_status_active_settings_and_pass_types()
        {
            var snap = await Build().BuildAsync();

            snap.Games.Should().NotBeNull();
            snap.Games!.Select(g => g.Id).Should().Equal(1, 2);
            snap.Games.Single(g => g.Id == 2).Status.Should().Be("Disabled");

            snap.Rooms.Should().NotBeNull();
            snap.Rooms!.Select(r => r.Id).Should().Equal(1, 2);
            var ps5 = snap.Rooms.Single(r => r.Id == 1);
            ps5.IsOpenSet.Should().BeFalse();
            ps5.Sets.Select(s => s.Name).Should().Equal("Station 1", "Station 2", "Station 3");
            ps5.Sets.Single(s => s.Id == 1).Should().BeEquivalentTo(new OfflineRoomSetDto(1, "Station 1", 10, "Occupied"));
            ps5.Sets.Single(s => s.Id == 2).Status.Should().Be("Free");
            ps5.Sets.Single(s => s.Id == 3).StatusId.Should().BeNull();
            var hall = snap.Rooms.Single(r => r.Id == 2);
            hall.IsOpenSet.Should().BeTrue();
            hall.Sets.Should().BeEmpty();

            snap.GameSettings.Should().NotBeNull();
            snap.GameSettings!.Select(s => s.Id).Should().Equal(1, 3);
            snap.GameSettings.Should().OnlyContain(s => s.IsActive);
            snap.GameSettings.Single(s => s.Id == 3).IsOpenHour.Should().BeTrue();

            snap.PassTypes.Should().ContainSingle(p => p.Name == "Day pass" && p.Price == 12m);

            // the existing F&B members are untouched
            snap.Categories.Should().ContainSingle(c => c.Id == 4);
            snap.Items.Should().BeEmpty();
            snap.Version.Should().HaveLength(16);
        }

        [Fact]
        public async Task Snapshot_version_changes_when_a_set_status_changes()
        {
            var before = (await Build().BuildAsync()).Version;

            var set = _db.Set<Set>().Single(s => s.Id == 2);
            set.StatusId = 10;
            _db.SaveChanges();

            var after = (await Build().BuildAsync()).Version;
            after.Should().NotBe(before);
        }
    }
}
