using Application.Common;
using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Services.Phase5
{
    public interface IBookingService
    {
        Task<BookingSettingsDto> SettingsAsync(CancellationToken ct = default);
        Task<List<BookableStationDto>> StationsAsync(CancellationToken ct = default);
        Task<BaseResponse<List<BookingSlotDto>>> AvailabilityAsync(int setId, DateTime date, int durationMinutes, CancellationToken ct = default);
        Task<BaseResponse<BookingDto>> CreateAsync(BookingCreateDto dto, bool byStaff, CancellationToken ct = default);
        Task<BookingPublicDto?> GetPublicAsync(string token, CancellationToken ct = default);
        Task<BaseResponse<BookingPublicDto>> CancelPublicAsync(string token, CancellationToken ct = default);
        Task<BaseResponse<BookingPublicDto>> CheckInPublicAsync(string token, CancellationToken ct = default);
        Task<List<BookingDto>> ListAsync(DateTime from, DateTime to, CancellationToken ct = default);
        Task<BaseResponse<BookingDto>> SetStatusAsync(int id, string status, CancellationToken ct = default);
        Task<int> MarkNoShowsAsync(CancellationToken ct = default);
    }

    /// <summary>
    /// Online booking of PS5 / board-game stations (Sets). A station is free for a
    /// slot when no Pending/Confirmed/CheckedIn booking overlaps it and no running
    /// game session is on it at that time. Guests check in with the QR link.
    /// Settings: Booking.OpenHour, Booking.CloseHour (24 = midnight), Booking.SlotMinutes,
    /// Booking.MaxDays, Booking.RequireApproval, Booking.RoomIds, Booking.NoShowMinutes, Venue.TimeZone.
    /// </summary>
    public class BookingService : IBookingService
    {
        private readonly ApplicationDbContext _db;
        private readonly IIntegrationSettingsService _settings;
        private readonly ICurrentUserAccessor _user;
        private readonly IOwnerNotifier _notify;
        private readonly ILogger<BookingService> _log;

        public BookingService(ApplicationDbContext db, IIntegrationSettingsService settings, ICurrentUserAccessor user, IOwnerNotifier notify, ILogger<BookingService> log)
        { _db = db; _settings = settings; _user = user; _notify = notify; _log = log; }

        private async Task<int> IntAsync(string key, int def, CancellationToken ct) => int.TryParse(await _settings.GetRawAsync(key, ct), out var v) ? v : def;

        public async Task<BookingSettingsDto> SettingsAsync(CancellationToken ct = default)
        {
            var tz = await _settings.GetRawAsync("Venue.TimeZone", ct) ?? "Asia/Beirut";
            return new(Math.Clamp(await IntAsync("Booking.OpenHour", 12, ct), 0, 23), Math.Clamp(await IntAsync("Booking.CloseHour", 24, ct), 1, 24),
                Math.Clamp(await IntAsync("Booking.SlotMinutes", 30, ct), 15, 120), Math.Clamp(await IntAsync("Booking.MaxDays", 14, ct), 1, 90),
                string.Equals((await _settings.GetRawAsync("Booking.RequireApproval", ct))?.Trim(), "true", StringComparison.OrdinalIgnoreCase), tz);
        }

        public async Task<List<BookableStationDto>> StationsAsync(CancellationToken ct = default)
        {
            var rooms = OwnerNotifier.SplitList(await _settings.GetRawAsync("Booking.RoomIds", ct)).Select(x => int.TryParse(x, out var v) ? v : 0).Where(v => v > 0).ToList();
            var q = _db.Set<Set>().AsNoTracking().Where(s => s.StatusId == null || s.StatusId == 1);
            if (rooms.Count > 0) q = q.Where(s => rooms.Contains(s.RoomId));
            return await q.OrderBy(s => s.Room.Name).ThenBy(s => s.Name).Select(s => new BookableStationDto(s.Id, s.Name, s.RoomId, s.Room.Name)).ToListAsync(ct);
        }

        public async Task<BaseResponse<List<BookingSlotDto>>> AvailabilityAsync(int setId, DateTime date, int durationMinutes, CancellationToken ct = default)
        {
            var cfg = await SettingsAsync(ct);
            if (!(await StationsAsync(ct)).Any(s => s.SetId == setId)) return new(false, "NotFound", "Station not found.");
            if (durationMinutes is < 30 or > 600) return new(false, "Validation", "Duration must be 30 to 600 minutes.");
            var tz = VenueClock.Zone(cfg.TimeZone);
            var dayStart = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(date.Date, DateTimeKind.Unspecified), tz);
            var open = dayStart.AddHours(cfg.OpenHour); var close = dayStart.AddHours(cfg.CloseHour);
            var bookings = await BlockingAsync(setId, open, close.AddMinutes(durationMinutes), null, ct);
            var running = await RunningUntilAsync(setId, ct);
            var now = DateTime.UtcNow;
            var slots = new List<BookingSlotDto>();
            for (var s = open; s.AddMinutes(durationMinutes) <= close; s = s.AddMinutes(cfg.SlotMinutes))
            {
                var e = s.AddMinutes(durationMinutes);
                var free = s > now && !bookings.Any(b => s < b.EndsOn && e > b.StartsOn) && !(running is DateTime ru && s < ru);
                slots.Add(new(s, e, free));
            }
            return new(true, null, "OK", slots);
        }

        private Task<List<StationBooking>> BlockingAsync(int setId, DateTime from, DateTime to, int? exceptId, CancellationToken ct)
            => _db.Set<StationBooking>().AsNoTracking()
                .Where(b => b.SetId == setId && BookingStatuses.Blocking.Contains(b.Status) && b.StartsOn < to && b.EndsOn > from && (exceptId == null || b.Id != exceptId))
                .ToListAsync(ct);

        /// <summary>End of the game session running on the station now (MaxValue when open-ended), or null.</summary>
        private async Task<DateTime?> RunningUntilAsync(int setId, CancellationToken ct)
        {
            var run = await _db.Set<TransactionRecord>().AsNoTracking()
                .Where(t => t.SetId == setId && t.GameId != null && StationBusy.RunningStatuses.Contains(t.StatusId))
                .Select(t => new { t.ExpectedEndOn }).ToListAsync(ct);
            if (run.Count == 0) return null;
            return run.Any(r => r.ExpectedEndOn == null) ? DateTime.MaxValue : run.Max(r => r.ExpectedEndOn);
        }

        public async Task<BaseResponse<BookingDto>> CreateAsync(BookingCreateDto dto, bool byStaff, CancellationToken ct = default)
        {
            var cfg = await SettingsAsync(ct);
            var station = (await StationsAsync(ct)).FirstOrDefault(s => s.SetId == dto.SetId);
            if (station is null) return Fail("NotFound", "Station not found.");
            var name = (dto.CustomerName ?? "").Trim(); var phone = (dto.Phone ?? "").Trim();
            if (name.Length is < 2 or > 120) return Fail("Validation", "Name is required.");
            if (phone.Count(char.IsDigit) < 7 || phone.Length > 40) return Fail("Validation", "A valid phone number is required.");
            if (dto.Players is < 1 or > 20) return Fail("Validation", "Players must be 1 to 20.");
            if (dto.DurationMinutes is < 30 or > 600) return Fail("Validation", "Duration must be 30 to 600 minutes.");
            var start = DateTime.SpecifyKind(dto.StartsOn, DateTimeKind.Utc);
            if (dto.StartsOn.Kind == DateTimeKind.Local) start = dto.StartsOn.ToUniversalTime();
            var end = start.AddMinutes(dto.DurationMinutes);
            var now = DateTime.UtcNow;
            if (start < now.AddMinutes(byStaff ? -5 : 10)) return Fail("Validation", "Pick a time in the future.");
            if (start > now.AddDays(cfg.MaxDays)) return Fail("Validation", $"Bookings open {cfg.MaxDays} days ahead.");
            var tz = VenueClock.Zone(cfg.TimeZone);
            var localStart = TimeZoneInfo.ConvertTimeFromUtc(start, tz); var localEnd = TimeZoneInfo.ConvertTimeFromUtc(end, tz);
            var dayStart = localStart.Date;
            if (!byStaff && (localStart < dayStart.AddHours(cfg.OpenHour) || localEnd > dayStart.AddHours(cfg.CloseHour)))
                return Fail("Validation", $"We are open {cfg.OpenHour:00}:00 to {cfg.CloseHour % 24:00}:00.");
            if ((await BlockingAsync(dto.SetId, start, end, null, ct)).Count > 0) return Fail("Taken", "This station is already booked at that time.");
            if (await RunningUntilAsync(dto.SetId, ct) is DateTime ru && start < ru) return Fail("Taken", "A game is running on this station until after that time.");
            if (!byStaff)
            {
                var digits = new string(phone.Where(char.IsDigit).ToArray());
                var active = await _db.Set<StationBooking>().CountAsync(b => b.Phone == phone && BookingStatuses.Blocking.Contains(b.Status) && b.EndsOn > now, ct);
                if (active >= 3) return Fail("Limit", "This phone number already has 3 upcoming bookings.");
            }
            var b = new StationBooking
            {
                SetId = dto.SetId, StartsOn = start, EndsOn = end, CustomerName = name, Phone = phone, Players = dto.Players,
                Notes = string.IsNullOrWhiteSpace(dto.Notes) ? null : dto.Notes.Trim()[..Math.Min(dto.Notes.Trim().Length, 500)],
                Status = !byStaff && cfg.RequireApproval ? BookingStatuses.Pending : BookingStatuses.Confirmed,
                Token = TournamentService.NewToken(), Source = byStaff ? "Staff" : "Web", HandledBy = byStaff ? _user.UserName : null, BranchId = _user.BranchId,
            };
            _db.Add(b);
            await _db.SaveChangesAsync(ct);
            if (!byStaff)
            {
                try { await _notify.NotifyOwnersAsync("New booking", $"{name} booked {station.Room} · {station.Station} at {localStart:ddd dd MMM HH:mm} for {dto.DurationMinutes} min ({dto.Players} players).", new[] { "InApp" }, null, ct); }
                catch (Exception ex) { _log.LogWarning(ex, "Booking notification failed"); }
            }
            return new(true, null, b.Status == BookingStatuses.Pending ? "Booking received: we will confirm it shortly." : "Booking confirmed.", (await ListByIdAsync(b.Id, ct))!);
        }

        private async Task<BookingDto?> ListByIdAsync(int id, CancellationToken ct)
            => (await Query(_db.Set<StationBooking>().AsNoTracking().Where(b => b.Id == id)).ToListAsync(ct)).FirstOrDefault();

        private IQueryable<BookingDto> Query(IQueryable<StationBooking> src)
            => from b in src
               join s in _db.Set<Set>().AsNoTracking() on b.SetId equals s.Id
               select new BookingDto(b.Id, b.SetId, s.Name, s.Room.Name, b.StartsOn, b.EndsOn, b.CustomerName, b.Phone, b.Players, b.Notes, b.Status, b.Token, b.Source, b.CreatedOn, b.CheckedInOn);

        public async Task<BookingPublicDto?> GetPublicAsync(string token, CancellationToken ct = default)
        {
            var b = (await Query(_db.Set<StationBooking>().AsNoTracking().Where(x => x.Token == token)).ToListAsync(ct)).FirstOrDefault();
            return b is null ? null : ToPublic(b);
        }

        private static BookingPublicDto ToPublic(BookingDto b)
        {
            var now = DateTime.UtcNow;
            var canCheckIn = b.Status == BookingStatuses.Confirmed && now >= b.StartsOn.AddMinutes(-30) && now <= b.EndsOn;
            var canCancel = (b.Status is BookingStatuses.Confirmed or BookingStatuses.Pending) && now < b.StartsOn.AddHours(-1);
            return new(b.Station, b.Room, b.StartsOn, b.EndsOn, b.CustomerName, b.Players, b.Status, b.Token, canCheckIn, canCancel);
        }

        public async Task<BaseResponse<BookingPublicDto>> CancelPublicAsync(string token, CancellationToken ct = default)
        {
            var b = await _db.Set<StationBooking>().FirstOrDefaultAsync(x => x.Token == token, ct);
            if (b is null) return new(false, "NotFound", "Booking not found.");
            var pub = (await GetPublicAsync(token, ct))!;
            if (!pub.CanCancel) return new(false, "Validation", "This booking can no longer be cancelled online (less than an hour to go).");
            b.Status = BookingStatuses.Cancelled;
            await _db.SaveChangesAsync(ct);
            return new(true, null, "Booking cancelled.", (await GetPublicAsync(token, ct))!);
        }

        public async Task<BaseResponse<BookingPublicDto>> CheckInPublicAsync(string token, CancellationToken ct = default)
        {
            var b = await _db.Set<StationBooking>().FirstOrDefaultAsync(x => x.Token == token, ct);
            if (b is null) return new(false, "NotFound", "Booking not found.");
            var pub = (await GetPublicAsync(token, ct))!;
            if (b.Status == BookingStatuses.CheckedIn) return new(true, null, "Already checked in.", pub);
            if (!pub.CanCheckIn) return new(false, "Validation", "Check-in opens 30 minutes before your booking.");
            b.Status = BookingStatuses.CheckedIn; b.CheckedInOn = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            try { await _notify.NotifyOwnersAsync("Guest arrived", $"{b.CustomerName} checked in for {pub.Room} · {pub.Station}. Start their session.", new[] { "InApp" }, null, ct); }
            catch (Exception ex) { _log.LogWarning(ex, "Check-in notification failed"); }
            return new(true, null, "Checked in. Staff will start your session.", (await GetPublicAsync(token, ct))!);
        }

        public async Task<List<BookingDto>> ListAsync(DateTime from, DateTime to, CancellationToken ct = default)
            => await Query(_db.Set<StationBooking>().AsNoTracking().Where(b => b.StartsOn < to && b.EndsOn > from)).OrderBy(b => b.StartsOn).ToListAsync(ct);

        public async Task<BaseResponse<BookingDto>> SetStatusAsync(int id, string status, CancellationToken ct = default)
        {
            var allowed = new[] { BookingStatuses.Confirmed, BookingStatuses.CheckedIn, BookingStatuses.Completed, BookingStatuses.NoShow, BookingStatuses.Cancelled };
            var to = allowed.FirstOrDefault(s => string.Equals(s, status, StringComparison.OrdinalIgnoreCase));
            if (to is null) return Fail("Validation", $"Status must be one of {string.Join(", ", allowed)}.");
            var b = await _db.Set<StationBooking>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (b is null) return Fail("NotFound", "Booking not found.");
            if (b.Status is BookingStatuses.Cancelled or BookingStatuses.Completed or BookingStatuses.NoShow && to == BookingStatuses.Confirmed
                && (await BlockingAsync(b.SetId, b.StartsOn, b.EndsOn, b.Id, ct)).Count > 0)
                return Fail("Taken", "The slot was booked by someone else meanwhile.");
            b.Status = to; b.HandledBy = _user.UserName;
            if (to == BookingStatuses.CheckedIn) b.CheckedInOn ??= DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            return new(true, null, $"Booking is now {to}.", (await ListByIdAsync(id, ct))!);
        }

        public async Task<int> MarkNoShowsAsync(CancellationToken ct = default)
        {
            var minutes = Math.Clamp(await IntAsync("Booking.NoShowMinutes", 30, ct), 5, 240);
            var cutoff = DateTime.UtcNow.AddMinutes(-minutes);
            var late = await _db.Set<StationBooking>().Where(b => b.Status == BookingStatuses.Confirmed && b.StartsOn < cutoff).ToListAsync(ct);
            foreach (var b in late) { b.Status = BookingStatuses.NoShow; b.HandledBy = "system"; }
            var done = await _db.Set<StationBooking>().Where(b => b.Status == BookingStatuses.CheckedIn && b.EndsOn < DateTime.UtcNow.AddHours(-1)).ToListAsync(ct);
            foreach (var b in done) b.Status = BookingStatuses.Completed;
            await _db.SaveChangesAsync(ct);
            return late.Count;
        }

        private static BaseResponse<BookingDto> Fail(string e, string m) => new(false, e, m);
    }

    public interface IStationOrderService
    {
        Task<List<StationCodeDto>> ListCodesAsync(CancellationToken ct = default);
        Task<StationCodeDto> EnsureCodeAsync(int setId, bool rotate, CancellationToken ct = default);
        Task<StationInfoDto?> GetAsync(string token, CancellationToken ct = default);
        Task<BaseResponse<StationOrderResultDto>> OrderAsync(string token, StationOrderDto dto, CancellationToken ct = default);
        Task<BaseResponse<bool>> RequestExtensionAsync(string token, int minutes, CancellationToken ct = default);
    }

    /// <summary>
    /// Food from the station: the QR on each PS5 / table opens the menu; orders go
    /// to one open F&amp;B invoice linked to that station (same Set), so the till
    /// sees it next to the game session and the kitchen gets it as usual.
    /// Station.RequireRunningSession (default true) refuses orders when no game is running.
    /// </summary>
    public class StationOrderService : IStationOrderService
    {
        private const int EnabledItemStatus = 1;
        private const int OpenInvoiceStatus = 7;
        private readonly ApplicationDbContext _db;
        private readonly ITransactionRecordService _trx;
        private readonly IIntegrationSettingsService _settings;
        private readonly IOwnerNotifier _notify;
        private readonly ILogger<StationOrderService> _log;

        public StationOrderService(ApplicationDbContext db, ITransactionRecordService trx, IIntegrationSettingsService settings, IOwnerNotifier notify, ILogger<StationOrderService> log)
        { _db = db; _trx = trx; _settings = settings; _notify = notify; _log = log; }

        public async Task<List<StationCodeDto>> ListCodesAsync(CancellationToken ct = default)
        {
            var sets = await _db.Set<Set>().AsNoTracking().OrderBy(s => s.Room.Name).ThenBy(s => s.Name).Select(s => new { s.Id, s.Name, Room = s.Room.Name }).ToListAsync(ct);
            var codes = await _db.Set<StationCode>().AsNoTracking().ToDictionaryAsync(c => c.SetId, ct);
            return sets.Select(s => codes.TryGetValue(s.Id, out var c) ? new StationCodeDto(s.Id, s.Name, s.Room, c.Token, c.IsActive) : new StationCodeDto(s.Id, s.Name, s.Room, "", false)).ToList();
        }

        public async Task<StationCodeDto> EnsureCodeAsync(int setId, bool rotate, CancellationToken ct = default)
        {
            var set = await _db.Set<Set>().AsNoTracking().Where(s => s.Id == setId).Select(s => new { s.Id, s.Name, Room = s.Room.Name }).FirstOrDefaultAsync(ct)
                ?? throw new KeyNotFoundException("Station not found.");
            var c = await _db.Set<StationCode>().FirstOrDefaultAsync(x => x.SetId == setId, ct);
            if (c is null) { c = new StationCode { SetId = setId, Token = TournamentService.NewToken() }; _db.Add(c); }
            else if (rotate) c.Token = TournamentService.NewToken();
            c.IsActive = true;
            await _db.SaveChangesAsync(ct);
            return new(set.Id, set.Name, set.Room, c.Token, c.IsActive);
        }

        private async Task<(StationCode code, Set set, string room)?> ResolveAsync(string token, CancellationToken ct)
        {
            var c = await _db.Set<StationCode>().AsNoTracking().FirstOrDefaultAsync(x => x.Token == token && x.IsActive, ct);
            if (c is null) return null;
            var s = await _db.Set<Set>().AsNoTracking().Include(x => x.Room).FirstOrDefaultAsync(x => x.Id == c.SetId, ct);
            return s is null ? null : (c, s, s.Room.Name);
        }

        public async Task<StationInfoDto?> GetAsync(string token, CancellationToken ct = default)
        {
            var r = await ResolveAsync(token, ct);
            if (r is null) return null;
            var (_, set, room) = r.Value;
            var session = await _db.Set<TransactionRecord>().AsNoTracking()
                .Where(t => t.SetId == set.Id && t.GameId != null && StationBusy.RunningStatuses.Contains(t.StatusId))
                .OrderByDescending(t => t.Id).Select(t => new { Game = t.Game!.Name, t.CreatedOn, t.ExpectedEndOn }).FirstOrDefaultAsync(ct);
            var inv = await OpenInvoiceAsync(set.Id, ct);
            var menu = await _db.Set<Item>().AsNoTracking()
                .Where(i => i.StatusId == EnabledItemStatus && i.Type == "CoffeeShop" && i.Category.ItemType != "Retail")
                .OrderBy(i => i.Category.Name).ThenBy(i => i.Name)
                .Select(i => new StationMenuItemDto(i.Id, i.Name, i.Price, i.ImagePath, i.Category.Name)).ToListAsync(ct);
            return new(set.Name, room, session is not null, session?.Game, session?.CreatedOn, session?.ExpectedEndOn, inv?.Id, inv?.TotalPrice ?? 0, menu);
        }

        private Task<TransactionRecord?> OpenInvoiceAsync(int setId, CancellationToken ct)
            => _db.Set<TransactionRecord>().AsNoTracking()
                .Where(t => t.SetId == setId && t.GameId == null && t.StatusId == OpenInvoiceStatus && t.Comment != null && t.Comment.StartsWith("Station order"))
                .OrderByDescending(t => t.Id).FirstOrDefaultAsync(ct);

        public async Task<BaseResponse<StationOrderResultDto>> OrderAsync(string token, StationOrderDto dto, CancellationToken ct = default)
        {
            var r = await ResolveAsync(token, ct);
            if (r is null) return new(false, "NotFound", "This QR code is not active.");
            var (_, set, room) = r.Value;
            var lines = (dto.Items ?? new()).Where(l => l.Quantity > 0).GroupBy(l => l.ItemId).Select(g => new OrderItemRequest(g.Key, g.Sum(x => x.Quantity))).ToList();
            if (lines.Count == 0) return new(false, "Validation", "Pick at least one item.");
            if (lines.Any(l => l.Quantity > 20) || lines.Count > 30) return new(false, "Validation", "That is a very big order: please ask the staff.");
            var ids = lines.Select(l => l.ItemId).ToList();
            var valid = await _db.Set<Item>().AsNoTracking().Where(i => ids.Contains(i.Id) && i.StatusId == EnabledItemStatus && i.Type == "CoffeeShop").Select(i => i.Id).ToListAsync(ct);
            if (valid.Count != ids.Count) return new(false, "Validation", "Some items are not available.");
            var requireSession = !string.Equals((await _settings.GetRawAsync("Station.RequireRunningSession", ct))?.Trim(), "false", StringComparison.OrdinalIgnoreCase);
            if (requireSession && !(await StationBusy.RunningSetIdsAsync(_db, ct)).Contains(set.Id))
                return new(false, "NoSession", "Ordering from the station works while your game is running. Please ask the staff.");

            var note = string.IsNullOrWhiteSpace(dto.Note) ? "" : " · " + dto.Note.Trim()[..Math.Min(dto.Note.Trim().Length, 200)];
            var existing = await OpenInvoiceAsync(set.Id, ct);
            BaseResponse<TransactionDto> res = existing is not null
                ? await _trx.AddItemsToOpenInvoice(existing.Id, lines, $"station:{set.Id}", ct)
                : await _trx.CreateCoffeeShopOrder(null, 0, lines, $"station:{set.Id}", ct, $"Station order · {room} · {set.Name}{note}", isOpenInvoice: true, setId: set.Id);
            if (!res.Success || res.Data is null) return new(false, res.Error ?? "Failed", res.Message);
            try { await _notify.NotifyOwnersAsync("Station order", $"{room} · {set.Name}: {lines.Sum(l => l.Quantity)} item(s){note}", new[] { "InApp" }, null, ct); }
            catch (Exception ex) { _log.LogWarning(ex, "Station order notification failed"); }
            return new(true, null, "Order sent to the kitchen.", new StationOrderResultDto(res.Data.Id, res.Data.TotalPrice, "Order sent to the kitchen."));
        }

        public async Task<BaseResponse<bool>> RequestExtensionAsync(string token, int minutes, CancellationToken ct = default)
        {
            var r = await ResolveAsync(token, ct);
            if (r is null) return new(false, "NotFound", "This QR code is not active.");
            if (minutes is not (15 or 30 or 60)) return new(false, "Validation", "Pick 15, 30 or 60 minutes.");
            var (_, set, room) = r.Value;
            await _notify.NotifyOwnersAsync("Extend session", $"{room} · {set.Name} asks for {minutes} more minutes.", new[] { "InApp" }, null, ct);
            return new(true, null, "The staff got your request.", true);
        }
    }

    public class BookingJobs
    {
        private readonly IBookingService _svc;
        public BookingJobs(IBookingService svc) { _svc = svc; }
        public Task MarkNoShowsAsync(CancellationToken ct) => _svc.MarkNoShowsAsync(ct);
    }
}
