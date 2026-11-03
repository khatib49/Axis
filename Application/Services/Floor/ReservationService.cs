using Application.Common;
using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Services.Floor
{
    public class ReservationService : IReservationService
    {
        private const int MaxPendingPerPhone = 3;
        private static readonly string[] Resources = { Reservation.ResourceTable, Reservation.ResourceRoom, Reservation.ResourceConsole };
        private static readonly string[] Sources = { "Phone", "WalkIn", "Online" };
        private static readonly string[] Blocking = { Reservation.StatusConfirmed, Reservation.StatusSeated };

        private readonly ApplicationDbContext _db;
        private readonly ITableSessionService _sessions;
        private readonly ICurrentUserAccessor _user;
        private readonly ILogger<ReservationService> _logger;

        public ReservationService(ApplicationDbContext db, ITableSessionService sessions, ICurrentUserAccessor user,
            ILogger<ReservationService> logger)
        {
            _db = db;
            _sessions = sessions;
            _user = user;
            _logger = logger;
        }

        // ── Queries ──────────────────────────────────────────────────
        public async Task<ReservationDto?> GetAsync(int id, CancellationToken ct = default)
        {
            var r = await _db.Set<Reservation>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            return r is null ? null : (await ToDtosAsync(new[] { r }, ct))[0];
        }

        public async Task<PaginatedResponse<ReservationDto>> ListAsync(ReservationListFilterDto filter, CancellationToken ct = default)
        {
            var q = _db.Set<Reservation>().AsNoTracking();
            if (filter.From.HasValue) { var f = Utc(filter.From.Value); q = q.Where(r => r.EndOn >= f); }
            if (filter.To.HasValue) { var t = Utc(filter.To.Value); q = q.Where(r => r.StartOn <= t); }
            if (!string.IsNullOrWhiteSpace(filter.Status)) q = q.Where(r => r.Status == filter.Status);
            if (!string.IsNullOrWhiteSpace(filter.ResourceType)) q = q.Where(r => r.ResourceType == filter.ResourceType);
            if (!string.IsNullOrWhiteSpace(filter.Search))
            {
                var s = filter.Search.Trim().ToLower();
                q = q.Where(r => r.CustomerName.ToLower().Contains(s) || r.Phone.Contains(s));
            }
            var page = filter.Page < 1 ? 1 : filter.Page;
            var size = filter.PageSize < 1 ? 50 : Math.Min(filter.PageSize, 500);
            var total = await q.CountAsync(ct);
            var rows = await q.OrderBy(r => r.StartOn).Skip((page - 1) * size).Take(size).ToListAsync(ct);
            return new PaginatedResponse<ReservationDto>(total, await ToDtosAsync(rows, ct), page, size);
        }

        public async Task<ReservationBoardDto> TodayBoardAsync(DateTime? day, CancellationToken ct = default)
        {
            var start = DateTime.SpecifyKind((day ?? DateTime.UtcNow).Date, DateTimeKind.Utc);
            var end = start.AddDays(1);
            var rows = await _db.Set<Reservation>().AsNoTracking()
                .Where(r => r.StartOn < end && r.EndOn > start)
                .OrderBy(r => r.StartOn).ToListAsync(ct);
            return new ReservationBoardDto(start,
                rows.Count(r => r.Status == Reservation.StatusPending),
                rows.Count(r => r.Status == Reservation.StatusConfirmed),
                rows.Count(r => r.Status == Reservation.StatusSeated),
                rows.Count(r => r.Status == Reservation.StatusNoShow),
                rows.Count(r => r.Status == Reservation.StatusCancelled),
                await ToDtosAsync(rows, ct));
        }

        // ── Create / update ──────────────────────────────────────────
        public async Task<BaseResponse<ReservationDto>> CreateAsync(ReservationUpsertDto dto, CancellationToken ct = default)
        {
            var err = await ValidateAsync(dto.CustomerName, dto.Phone, dto.ResourceType, dto.StartOn, dto.EndOn, dto.PartySize,
                dto.TableId, dto.RoomId, dto.GameId, ct);
            if (err != null) return Fail(err);
            if (!Sources.Contains(dto.Source)) return Fail("Source must be Phone, WalkIn or Online.");

            var r = new Reservation
            {
                CustomerName = dto.CustomerName.Trim(),
                Phone = dto.Phone.Trim(),
                UserId = dto.UserId is > 0 ? dto.UserId : null,
                ResourceType = dto.ResourceType,
                TableId = dto.ResourceType == Reservation.ResourceTable ? dto.TableId : null,
                RoomId = dto.ResourceType == Reservation.ResourceRoom ? dto.RoomId : null,
                GameId = dto.ResourceType == Reservation.ResourceConsole ? dto.GameId : null,
                StartOn = Utc(dto.StartOn),
                EndOn = Utc(dto.EndOn),
                PartySize = dto.PartySize,
                Status = Reservation.StatusPending,
                Notes = dto.Notes,
                Source = dto.Source,
                CreatedOn = DateTime.UtcNow,
                CreatedBy = _user.UserName,
                BranchId = _user.BranchId,
            };

            if (dto.ConfirmNow)
            {
                var avail = await CheckAvailabilityAsync(new AvailabilityQueryDto(r.ResourceType, r.StartOn, r.EndOn, r.TableId, r.RoomId, r.GameId), ct);
                if (!avail.Available) return Fail($"Resource is already booked in that window ({avail.Conflicts.Count} conflict(s)).");
                r.Status = Reservation.StatusConfirmed;
                r.ConfirmedOn = DateTime.UtcNow;
            }

            _db.Add(r);
            await _db.SaveChangesAsync(ct);
            return Ok(await ToDtoAsync(r, ct), "Reservation created");
        }

        public async Task<BaseResponse<ReservationDto>> UpdateAsync(int id, ReservationUpsertDto dto, CancellationToken ct = default)
        {
            var r = await _db.Set<Reservation>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (r is null) return Fail("Reservation not found.");
            if (r.Status is Reservation.StatusCancelled or Reservation.StatusCompleted or Reservation.StatusNoShow)
                return Fail($"A {r.Status} reservation cannot be edited.");
            var err = await ValidateAsync(dto.CustomerName, dto.Phone, dto.ResourceType, dto.StartOn, dto.EndOn, dto.PartySize,
                dto.TableId, dto.RoomId, dto.GameId, ct);
            if (err != null) return Fail(err);

            var tableId = dto.ResourceType == Reservation.ResourceTable ? dto.TableId : null;
            var roomId = dto.ResourceType == Reservation.ResourceRoom ? dto.RoomId : null;
            var gameId = dto.ResourceType == Reservation.ResourceConsole ? dto.GameId : null;
            if (Blocking.Contains(r.Status))
            {
                var avail = await CheckAvailabilityAsync(new AvailabilityQueryDto(dto.ResourceType, Utc(dto.StartOn), Utc(dto.EndOn), tableId, roomId, gameId, r.Id), ct);
                if (!avail.Available) return Fail($"Resource is already booked in that window ({avail.Conflicts.Count} conflict(s)).");
            }

            r.CustomerName = dto.CustomerName.Trim();
            r.Phone = dto.Phone.Trim();
            if (dto.UserId.HasValue) r.UserId = dto.UserId > 0 ? dto.UserId : null;
            r.ResourceType = dto.ResourceType;
            r.TableId = tableId; r.RoomId = roomId; r.GameId = gameId;
            r.StartOn = Utc(dto.StartOn);
            r.EndOn = Utc(dto.EndOn);
            r.PartySize = dto.PartySize;
            r.Notes = dto.Notes;
            if (Sources.Contains(dto.Source)) r.Source = dto.Source;
            await _db.SaveChangesAsync(ct);
            return Ok(await ToDtoAsync(r, ct), "Reservation updated");
        }

        public async Task<BaseResponse<bool>> DeleteAsync(int id, CancellationToken ct = default)
        {
            var r = await _db.Set<Reservation>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (r is null) return new BaseResponse<bool>(false, "Not found", "Reservation not found.");
            if (r.Status == Reservation.StatusSeated) return new BaseResponse<bool>(false, "Validation", "A seated reservation cannot be deleted.");
            _db.Remove(r);
            await _db.SaveChangesAsync(ct);
            return new BaseResponse<bool>(true, null, "Deleted", true);
        }

        // ── Availability ─────────────────────────────────────────────
        public async Task<AvailabilityResultDto> CheckAvailabilityAsync(AvailabilityQueryDto q, CancellationToken ct = default)
        {
            var start = Utc(q.StartOn);
            var end = Utc(q.EndOn);
            var query = _db.Set<Reservation>().AsNoTracking()
                .Where(r => r.ResourceType == q.ResourceType && Blocking.Contains(r.Status)
                            && r.StartOn < end && r.EndOn > start);
            if (q.ExcludeReservationId.HasValue) query = query.Where(r => r.Id != q.ExcludeReservationId.Value);
            query = q.ResourceType switch
            {
                Reservation.ResourceTable => query.Where(r => r.TableId == q.TableId),
                Reservation.ResourceRoom => query.Where(r => r.RoomId == q.RoomId),
                Reservation.ResourceConsole => query.Where(r => r.GameId == q.GameId),
                _ => query.Where(r => false),
            };
            // A resource that is not specified cannot clash with anything.
            var resourceId = q.ResourceType switch
            {
                Reservation.ResourceTable => q.TableId,
                Reservation.ResourceRoom => q.RoomId,
                Reservation.ResourceConsole => q.GameId,
                _ => null,
            };
            if (resourceId is null or 0) return new AvailabilityResultDto(true, new List<ReservationDto>());

            var conflicts = await query.OrderBy(r => r.StartOn).ToListAsync(ct);
            return new AvailabilityResultDto(conflicts.Count == 0, await ToDtosAsync(conflicts, ct));
        }

        // ── Status transitions ───────────────────────────────────────
        public async Task<BaseResponse<ReservationDto>> ConfirmAsync(int id, CancellationToken ct = default)
        {
            var r = await _db.Set<Reservation>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (r is null) return Fail("Reservation not found.");
            if (r.Status != Reservation.StatusPending) return Fail($"Only a Pending reservation can be confirmed (current: {r.Status}).");
            var avail = await CheckAvailabilityAsync(new AvailabilityQueryDto(r.ResourceType, r.StartOn, r.EndOn, r.TableId, r.RoomId, r.GameId, r.Id), ct);
            if (!avail.Available) return Fail($"Resource is already booked in that window ({avail.Conflicts.Count} conflict(s)).");
            r.Status = Reservation.StatusConfirmed;
            r.ConfirmedOn = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            return Ok(await ToDtoAsync(r, ct), "Confirmed");
        }

        public async Task<BaseResponse<ReservationDto>> SeatAsync(int id, SeatReservationRequestDto dto, CancellationToken ct = default)
        {
            var r = await _db.Set<Reservation>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (r is null) return Fail("Reservation not found.");
            if (r.Status is not (Reservation.StatusPending or Reservation.StatusConfirmed))
                return Fail($"Only a Pending or Confirmed reservation can be seated (current: {r.Status}).");

            if (dto.OpenTableSession)
            {
                if (r.ResourceType != Reservation.ResourceTable || r.TableId is null)
                    return Fail("Only a table reservation can open a table session.");
                var open = await _sessions.OpenTableAsync(new OpenTableRequestDto(r.TableId.Value, dto.GuestCount ?? r.PartySize, r.UserId,
                    $"Reservation #{r.Id} — {r.CustomerName}"), ct);
                if (!open.Success || open.Data is null) return Fail(open.Message);
                r.SeatedTransactionId = open.Data.TransactionId;
            }

            r.Status = Reservation.StatusSeated;
            r.ConfirmedOn ??= DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            return Ok(await ToDtoAsync(r, ct), "Seated");
        }

        public async Task<BaseResponse<ReservationDto>> NoShowAsync(int id, CancellationToken ct = default)
        {
            var r = await _db.Set<Reservation>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (r is null) return Fail("Reservation not found.");
            if (r.Status is not (Reservation.StatusPending or Reservation.StatusConfirmed))
                return Fail($"Only a Pending or Confirmed reservation can be marked no-show (current: {r.Status}).");
            r.Status = Reservation.StatusNoShow;
            await _db.SaveChangesAsync(ct);
            return Ok(await ToDtoAsync(r, ct), "Marked as no-show");
        }

        public async Task<BaseResponse<ReservationDto>> CancelAsync(int id, CancellationToken ct = default)
        {
            var r = await _db.Set<Reservation>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (r is null) return Fail("Reservation not found.");
            if (r.Status is Reservation.StatusSeated or Reservation.StatusCompleted)
                return Fail($"A {r.Status} reservation cannot be cancelled.");
            r.Status = Reservation.StatusCancelled;
            await _db.SaveChangesAsync(ct);
            return Ok(await ToDtoAsync(r, ct), "Cancelled");
        }

        public async Task<BaseResponse<ReservationDto>> CompleteAsync(int id, CancellationToken ct = default)
        {
            var r = await _db.Set<Reservation>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (r is null) return Fail("Reservation not found.");
            if (r.Status != Reservation.StatusSeated) return Fail("Only a Seated reservation can be completed.");
            r.Status = Reservation.StatusCompleted;
            await _db.SaveChangesAsync(ct);
            return Ok(await ToDtoAsync(r, ct), "Completed");
        }

        // ── Public request ───────────────────────────────────────────
        public async Task<BaseResponse<ReservationDto>> PublicRequestAsync(PublicReservationRequestDto dto, CancellationToken ct = default)
        {
            var err = await ValidateAsync(dto.CustomerName, dto.Phone, dto.ResourceType, dto.StartOn, dto.EndOn, dto.PartySize,
                dto.TableId, dto.RoomId, dto.GameId, ct, requireResource: false);
            if (err != null) return Fail(err);
            if (Utc(dto.StartOn) < DateTime.UtcNow.AddMinutes(-5)) return Fail("Start time must be in the future.");
            if (Utc(dto.StartOn) > DateTime.UtcNow.AddDays(90)) return Fail("Reservations can be requested at most 90 days ahead.");
            if (dto.PartySize > 50) return Fail("Party size too large — please call the venue.");

            var phone = dto.Phone.Trim();
            var pending = await _db.Set<Reservation>().CountAsync(r => r.Phone == phone && r.Status == Reservation.StatusPending, ct);
            if (pending >= MaxPendingPerPhone)
                return Fail($"You already have {pending} pending requests. Please wait for confirmation or call us.");

            var r = new Reservation
            {
                CustomerName = dto.CustomerName.Trim(),
                Phone = phone,
                ResourceType = dto.ResourceType,
                TableId = dto.ResourceType == Reservation.ResourceTable ? dto.TableId : null,
                RoomId = dto.ResourceType == Reservation.ResourceRoom ? dto.RoomId : null,
                GameId = dto.ResourceType == Reservation.ResourceConsole ? dto.GameId : null,
                StartOn = Utc(dto.StartOn),
                EndOn = Utc(dto.EndOn),
                PartySize = dto.PartySize,
                Status = Reservation.StatusPending,
                Notes = dto.Notes is null ? null : dto.Notes.Length > 500 ? dto.Notes[..500] : dto.Notes,
                Source = "Online",
                CreatedOn = DateTime.UtcNow,
                CreatedBy = "online",
            };
            _db.Add(r);
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Online reservation request #{Id} from {Phone} for {Start:u}", r.Id, phone, r.StartOn);
            return Ok(await ToDtoAsync(r, ct), "Request received — we will confirm shortly.");
        }

        // ── helpers ──────────────────────────────────────────────────
        private async Task<string?> ValidateAsync(string? name, string? phone, string resourceType, DateTime start, DateTime end,
            int partySize, int? tableId, int? roomId, int? gameId, CancellationToken ct, bool requireResource = true)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Customer name is required.";
            if (name.Trim().Length > 120) return "Customer name is too long.";
            if (string.IsNullOrWhiteSpace(phone) || phone.Trim().Length < 6) return "A valid phone number is required.";
            if (phone.Trim().Length > 40) return "Phone number is too long.";
            if (!Resources.Contains(resourceType)) return "Resource type must be Table, Room or Console.";
            if (Utc(end) <= Utc(start)) return "End time must be after start time.";
            if ((Utc(end) - Utc(start)).TotalHours > 24) return "A reservation cannot exceed 24 hours.";
            if (partySize <= 0) return "Party size must be at least 1.";

            switch (resourceType)
            {
                case Reservation.ResourceTable:
                    if (tableId is > 0)
                    {
                        if (!await _db.Set<DiningTable>().AnyAsync(t => t.Id == tableId && t.IsActive, ct)) return "Table not found.";
                    }
                    else if (requireResource) return "Table is required.";
                    break;
                case Reservation.ResourceRoom:
                    if (roomId is > 0)
                    {
                        if (!await _db.Set<Room>().AnyAsync(x => x.Id == roomId, ct)) return "Room not found.";
                    }
                    else if (requireResource) return "Room is required.";
                    break;
                case Reservation.ResourceConsole:
                    if (gameId is > 0)
                    {
                        if (!await _db.Set<Game>().AnyAsync(x => x.Id == gameId, ct)) return "Game/console not found.";
                    }
                    else if (requireResource) return "Game/console is required.";
                    break;
            }
            return null;
        }

        private static DateTime Utc(DateTime d) => d.Kind switch
        {
            DateTimeKind.Utc => d,
            DateTimeKind.Local => d.ToUniversalTime(),
            _ => DateTime.SpecifyKind(d, DateTimeKind.Utc),
        };

        private async Task<ReservationDto> ToDtoAsync(Reservation r, CancellationToken ct) => (await ToDtosAsync(new[] { r }, ct))[0];

        private async Task<List<ReservationDto>> ToDtosAsync(IReadOnlyCollection<Reservation> rows, CancellationToken ct)
        {
            var tableIds = rows.Where(r => r.TableId.HasValue).Select(r => r.TableId!.Value).Distinct().ToList();
            var roomIds = rows.Where(r => r.RoomId.HasValue).Select(r => r.RoomId!.Value).Distinct().ToList();
            var gameIds = rows.Where(r => r.GameId.HasValue).Select(r => r.GameId!.Value).Distinct().ToList();
            var tables = tableIds.Count == 0 ? new Dictionary<int, DiningTable>() :
                await _db.Set<DiningTable>().AsNoTracking().Where(t => tableIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, ct);
            var rooms = roomIds.Count == 0 ? new Dictionary<int, string>() :
                await _db.Set<Room>().AsNoTracking().Where(x => roomIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
            var games = gameIds.Count == 0 ? new Dictionary<int, string>() :
                await _db.Set<Game>().AsNoTracking().Where(x => gameIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, ct);

            return rows.Select(r =>
            {
                DiningTable? t = r.TableId.HasValue && tables.TryGetValue(r.TableId.Value, out var tt) ? tt : null;
                var resourceName = r.ResourceType switch
                {
                    Reservation.ResourceTable => t is null ? null : $"{t.Code} — {t.Name}",
                    Reservation.ResourceRoom => r.RoomId.HasValue && rooms.TryGetValue(r.RoomId.Value, out var rn) ? rn : null,
                    Reservation.ResourceConsole => r.GameId.HasValue && games.TryGetValue(r.GameId.Value, out var gn) ? gn : null,
                    _ => null,
                };
                return new ReservationDto(r.Id, r.CustomerName, r.Phone, r.UserId, r.ResourceType, r.TableId, t?.Code, r.RoomId, r.GameId,
                    resourceName, r.StartOn, r.EndOn, r.PartySize, r.Status, r.Notes, r.Source, r.CreatedOn, r.CreatedBy, r.ConfirmedOn,
                    r.SeatedTransactionId, r.BranchId);
            }).ToList();
        }

        private static BaseResponse<ReservationDto> Fail(string message) => new(false, "Validation", message);
        private static BaseResponse<ReservationDto> Ok(ReservationDto dto, string message) => new(true, null, message, dto);
    }
}
