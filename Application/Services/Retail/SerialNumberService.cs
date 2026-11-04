using Application.Common;
using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Services.Retail
{
    public class SerialNumberService : ISerialNumberService
    {
        private readonly ApplicationDbContext _db;
        private readonly ICurrentUserAccessor _user;
        private readonly ILogger<SerialNumberService> _logger;

        public SerialNumberService(ApplicationDbContext db, ICurrentUserAccessor user, ILogger<SerialNumberService> logger)
        {
            _db = db; _user = user; _logger = logger;
        }

        // ── Settings ───────────────────────────────────────────────────
        private IQueryable<SerialSettingDto> ProjectSettings(IQueryable<SerialSetting> q)
            => from s in q
               join i in _db.Set<Item>() on s.ItemId equals i.Id
               select new SerialSettingDto(s.ItemId, i.Name, s.RequiresSerial, s.Prefix, s.ModifiedOn, s.ModifiedBy);

        public async Task<List<SerialSettingDto>> ListSettingsAsync(CancellationToken ct = default)
            => await ProjectSettings(_db.Set<SerialSetting>().AsNoTracking().Where(s => s.RequiresSerial))
                .OrderBy(s => s.ItemName).ToListAsync(ct);

        public async Task<SerialSettingDto?> GetSettingAsync(int itemId, CancellationToken ct = default)
        {
            var s = await ProjectSettings(_db.Set<SerialSetting>().AsNoTracking().Where(x => x.ItemId == itemId)).FirstOrDefaultAsync(ct);
            if (s != null) return s;
            var item = await _db.Set<Item>().AsNoTracking().FirstOrDefaultAsync(i => i.Id == itemId, ct);
            return item == null ? null : new SerialSettingDto(itemId, item.Name, false, null, null, null);
        }

        public async Task<BaseResponse<SerialSettingDto>> SetSettingAsync(int itemId, SerialSettingUpsertDto dto, CancellationToken ct = default)
        {
            if (!await _db.Set<Item>().AnyAsync(i => i.Id == itemId, ct)) return Fail<SerialSettingDto>("Item not found.");
            var prefix = string.IsNullOrWhiteSpace(dto.Prefix) ? null : dto.Prefix.Trim().ToUpperInvariant();
            if (prefix != null && prefix.Length > 20) return Fail<SerialSettingDto>("Prefix is longer than 20 characters.");
            if (prefix != null && prefix.Any(char.IsWhiteSpace)) return Fail<SerialSettingDto>("Prefix cannot contain spaces.");

            var row = await _db.Set<SerialSetting>().FirstOrDefaultAsync(s => s.ItemId == itemId, ct);
            if (row == null)
            {
                row = new SerialSetting { ItemId = itemId, CreatedOn = DateTime.UtcNow };
                _db.Set<SerialSetting>().Add(row);
            }
            row.RequiresSerial = dto.RequiresSerial;
            row.Prefix = prefix;
            row.ModifiedOn = DateTime.UtcNow;
            row.ModifiedBy = _user.UserName;
            await _db.SaveChangesAsync(ct);
            return new(true, null, "Serial settings saved.", await GetSettingAsync(itemId, ct));
        }

        // ── Receive / list ─────────────────────────────────────────────
        private IQueryable<SerialNumberDto> Project(IQueryable<SerialNumber> q)
            => from s in q
               join i in _db.Set<Item>() on s.ItemId equals i.Id
               join v0 in _db.Set<ItemVariant>() on s.VariantId equals (int?)v0.Id into vj
               from v in vj.DefaultIfEmpty()
               select new SerialNumberDto(s.Id, s.ItemId, i.Name, s.VariantId, v != null ? v.Name : null,
                   s.Serial, s.Status, s.TransactionId, s.ReceivedOn, s.SoldOn, s.ReturnedOn, s.Notes, s.BranchId);

        public async Task<BaseResponse<SerialReceiveResultDto>> ReceiveAsync(SerialReceiveDto dto, CancellationToken ct = default)
        {
            var item = await _db.Set<Item>().AsNoTracking().FirstOrDefaultAsync(i => i.Id == dto.ItemId, ct);
            if (item == null) return Fail<SerialReceiveResultDto>("Item not found.");
            if (dto.VariantId.HasValue &&
                !await _db.Set<ItemVariant>().AnyAsync(v => v.Id == dto.VariantId.Value && v.ItemId == dto.ItemId, ct))
                return Fail<SerialReceiveResultDto>("Option does not belong to this item.");

            var wanted = (dto.Serials ?? new List<string>())
                .Select(Ean13.Normalise).Where(s => s.Length > 0)
                .Select(s => s.ToUpperInvariant()).Distinct().ToList();
            var count = dto.Count ?? 0;
            if (wanted.Count == 0 && count <= 0) return Fail<SerialReceiveResultDto>("Give a list of serials or a count to generate.");
            if (count > 500) return Fail<SerialReceiveResultDto>("At most 500 serials per receipt.");
            if (wanted.Any(s => s.Length > 80)) return Fail<SerialReceiveResultDto>("A serial is longer than 80 characters.");

            var skipped = new List<string>();
            var created = new List<string>();
            var notes = string.IsNullOrWhiteSpace(dto.Notes) ? null : dto.Notes.Trim();
            var now = DateTime.UtcNow;

            if (wanted.Count > 0)
            {
                var taken = await _db.Set<SerialNumber>().Where(s => wanted.Contains(s.Serial)).Select(s => s.Serial).ToListAsync(ct);
                foreach (var s in wanted)
                {
                    if (taken.Contains(s)) { skipped.Add(s); continue; }
                    _db.Set<SerialNumber>().Add(NewUnit(dto, s, notes, now));
                    created.Add(s);
                }
            }

            if (count > 0)
            {
                var setting = await _db.Set<SerialSetting>().AsNoTracking().FirstOrDefaultAsync(s => s.ItemId == dto.ItemId, ct);
                var prefix = string.IsNullOrWhiteSpace(setting?.Prefix) ? "SN" : setting!.Prefix!;
                var stem = $"{prefix}-{dto.ItemId:D4}-";
                var existing = await _db.Set<SerialNumber>().Where(s => s.Serial.StartsWith(stem)).Select(s => s.Serial).ToListAsync(ct);
                var next = 0;
                foreach (var s in existing)
                    if (int.TryParse(s[stem.Length..], out var n) && n > next) next = n;
                for (var k = 0; k < count; k++)
                {
                    next++;
                    var serial = $"{stem}{next:D5}";
                    _db.Set<SerialNumber>().Add(NewUnit(dto, serial, notes, now));
                    created.Add(serial);
                }
            }

            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Received {Count} serials for item {ItemId} by {User}", created.Count, dto.ItemId, _user.UserName);
            var msg = skipped.Count == 0 ? $"{created.Count} serial(s) received." : $"{created.Count} received, {skipped.Count} already existed.";
            return new(true, null, msg, new SerialReceiveResultDto(created.Count, created, skipped));
        }

        private SerialNumber NewUnit(SerialReceiveDto dto, string serial, string? notes, DateTime now) => new()
        {
            ItemId = dto.ItemId, VariantId = dto.VariantId, Serial = serial, Status = SerialStatus.InStock,
            ReceivedOn = now, Notes = notes, BranchId = _user.BranchId, CreatedBy = _user.UserName,
        };

        public async Task<List<SerialNumberDto>> ListAsync(int? itemId, string? status, string? search, int? transactionId, CancellationToken ct = default)
        {
            var q = _db.Set<SerialNumber>().AsNoTracking();
            if (itemId.HasValue) q = q.Where(s => s.ItemId == itemId.Value);
            if (transactionId.HasValue) q = q.Where(s => s.TransactionId == transactionId.Value);
            if (!string.IsNullOrWhiteSpace(status))
            {
                var st = SerialStatus.All.FirstOrDefault(x => string.Equals(x, status.Trim(), StringComparison.OrdinalIgnoreCase));
                if (st != null) q = q.Where(s => s.Status == st);
            }
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = Ean13.Normalise(search).ToUpperInvariant();
                q = q.Where(s => s.Serial.Contains(term));
            }
            return await Project(q.OrderByDescending(s => s.ReceivedOn).ThenByDescending(s => s.Id)).Take(500).ToListAsync(ct);
        }

        public async Task<SerialNumberDto?> GetAsync(int id, CancellationToken ct = default)
            => await Project(_db.Set<SerialNumber>().AsNoTracking().Where(s => s.Id == id)).FirstOrDefaultAsync(ct);

        // ── Status transitions ─────────────────────────────────────────
        public async Task<BaseResponse<SerialNumberDto>> SellAsync(SerialSellDto dto, CancellationToken ct = default)
        {
            var serial = Ean13.Normalise(dto.Serial).ToUpperInvariant();
            if (serial.Length == 0) return Fail<SerialNumberDto>("Serial is required.");
            if (dto.TransactionId <= 0) return Fail<SerialNumberDto>("Invoice id is required.");

            var row = await _db.Set<SerialNumber>().FirstOrDefaultAsync(s => s.Serial == serial, ct);
            if (row == null) return Fail<SerialNumberDto>($"Serial {serial} is unknown.");

            if (row.Status == SerialStatus.Sold)
            {
                if (row.TransactionId == dto.TransactionId)
                    return new(true, null, "Serial already recorded on this invoice.", await GetAsync(row.Id, ct));
                return Fail<SerialNumberDto>($"Serial {serial} was already sold on invoice #{row.TransactionId}.");
            }
            if (row.Status != SerialStatus.InStock)
                return Fail<SerialNumberDto>($"Serial {serial} is {row.Status} and cannot be sold.");

            if (!await _db.Set<TransactionRecord>().AnyAsync(t => t.Id == dto.TransactionId, ct))
                return Fail<SerialNumberDto>($"Invoice #{dto.TransactionId} not found.");

            row.Status = SerialStatus.Sold;
            row.TransactionId = dto.TransactionId;
            row.SoldOn = DateTime.UtcNow;
            row.ModifiedOn = row.SoldOn;
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Serial {Serial} sold on invoice {Tx} by {User}", serial, dto.TransactionId, _user.UserName);
            return new(true, null, $"Serial {serial} recorded on invoice #{dto.TransactionId}.", await GetAsync(row.Id, ct));
        }

        public async Task<BaseResponse<SerialNumberDto>> ReturnAsync(int id, SerialStatusChangeDto? dto, CancellationToken ct = default)
        {
            var row = await _db.Set<SerialNumber>().FirstOrDefaultAsync(s => s.Id == id, ct);
            if (row == null) return Fail<SerialNumberDto>("Serial not found.");
            if (row.Status != SerialStatus.Sold)
                return Fail<SerialNumberDto>($"Only a Sold unit can be returned (this one is {row.Status}).");
            row.Status = SerialStatus.Returned;
            row.ReturnedOn = DateTime.UtcNow;
            row.ModifiedOn = row.ReturnedOn;
            AppendNote(row, dto?.Notes, "Returned");
            await _db.SaveChangesAsync(ct);
            return new(true, null, "Unit marked as returned.", await GetAsync(id, ct));
        }

        public async Task<BaseResponse<SerialNumberDto>> MarkFaultyAsync(int id, SerialStatusChangeDto? dto, CancellationToken ct = default)
        {
            var row = await _db.Set<SerialNumber>().FirstOrDefaultAsync(s => s.Id == id, ct);
            if (row == null) return Fail<SerialNumberDto>("Serial not found.");
            if (row.Status == SerialStatus.Sold)
                return Fail<SerialNumberDto>("A sold unit must be returned before it can be marked faulty.");
            if (row.Status == SerialStatus.Faulty)
                return new(true, null, "Unit is already faulty.", await GetAsync(id, ct));
            row.Status = SerialStatus.Faulty;
            row.ModifiedOn = DateTime.UtcNow;
            AppendNote(row, dto?.Notes, "Faulty");
            await _db.SaveChangesAsync(ct);
            return new(true, null, "Unit marked as faulty.", await GetAsync(id, ct));
        }

        public async Task<BaseResponse<SerialNumberDto>> RestockAsync(int id, SerialStatusChangeDto? dto, CancellationToken ct = default)
        {
            var row = await _db.Set<SerialNumber>().FirstOrDefaultAsync(s => s.Id == id, ct);
            if (row == null) return Fail<SerialNumberDto>("Serial not found.");
            if (row.Status == SerialStatus.InStock) return new(true, null, "Unit is already in stock.", await GetAsync(id, ct));
            if (row.Status == SerialStatus.Sold)
                return Fail<SerialNumberDto>("A sold unit must be returned before it goes back to stock.");
            row.Status = SerialStatus.InStock;
            row.ModifiedOn = DateTime.UtcNow;
            AppendNote(row, dto?.Notes, "Restocked");
            await _db.SaveChangesAsync(ct);
            return new(true, null, "Unit back in stock.", await GetAsync(id, ct));
        }

        private static void AppendNote(SerialNumber row, string? note, string action)
        {
            if (string.IsNullOrWhiteSpace(note)) return;
            var line = $"{action}: {note.Trim()}";
            row.Notes = string.IsNullOrWhiteSpace(row.Notes) ? line : $"{row.Notes}\n{line}";
            if (row.Notes.Length > 500) row.Notes = row.Notes[^500..];
        }

        // ── Summary (stock-count helper) ───────────────────────────────
        public async Task<List<SerialSummaryDto>> SummaryAsync(int? itemId, CancellationToken ct = default)
        {
            var serialsQ = _db.Set<SerialNumber>().AsNoTracking();
            if (itemId.HasValue) serialsQ = serialsQ.Where(s => s.ItemId == itemId.Value);
            var counts = await serialsQ
                .GroupBy(s => new { s.ItemId, s.Status })
                .Select(g => new { g.Key.ItemId, g.Key.Status, Count = g.Count() })
                .ToListAsync(ct);
            var settings = await _db.Set<SerialSetting>().AsNoTracking()
                .Where(s => !itemId.HasValue || s.ItemId == itemId.Value).ToListAsync(ct);

            var ids = counts.Select(c => c.ItemId).Union(settings.Where(s => s.RequiresSerial).Select(s => s.ItemId)).Distinct().ToList();
            if (ids.Count == 0) return new();
            var items = await _db.Set<Item>().AsNoTracking().Where(i => ids.Contains(i.Id)).ToListAsync(ct);

            var result = new List<SerialSummaryDto>();
            foreach (var item in items.OrderBy(i => i.Name))
            {
                int Of(string st) => counts.Where(c => c.ItemId == item.Id && c.Status == st).Sum(c => c.Count);
                var setting = settings.FirstOrDefault(s => s.ItemId == item.Id);
                var inStock = Of(SerialStatus.InStock);
                result.Add(new SerialSummaryDto(item.Id, item.Name, setting?.RequiresSerial ?? false, setting?.Prefix,
                    inStock, Of(SerialStatus.Sold), Of(SerialStatus.Returned), Of(SerialStatus.Faulty),
                    item.Quantity, inStock - item.Quantity));
            }
            return result;
        }

        private static BaseResponse<T> Fail<T>(string msg) => new(false, msg, msg);
    }
}
