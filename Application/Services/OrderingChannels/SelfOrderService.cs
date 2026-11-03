using Application.Common;
using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Services.OrderingChannels
{
    /// <summary>
    /// QR self-ordering: a guest scans the table code, gets a session token,
    /// submits rounds of items. The first submit opens an F&B invoice via
    /// CreateCoffeeShopOrder(isOpenInvoice: true); later rounds go through
    /// AddItemsToOpenInvoice. The till closes the invoice as usual.
    /// </summary>
    public class SelfOrderService : ISelfOrderService
    {
        public const string PublicBaseUrlSettingKey = "SelfOrder.PublicBaseUrl";
        public const string Actor = "selforder";

        private readonly ApplicationDbContext _db;
        private readonly ITransactionRecordService _trx;
        private readonly ICurrentUserAccessor _user;
        private readonly IIntegrationSettingsService _settings;
        private readonly PublicMenuBuilder _menu;
        private readonly ILogger<SelfOrderService> _logger;

        public SelfOrderService(
            ApplicationDbContext db,
            ITransactionRecordService trx,
            ICurrentUserAccessor user,
            IIntegrationSettingsService settings,
            PublicMenuBuilder menu,
            ILogger<SelfOrderService> logger)
        {
            _db = db;
            _trx = trx;
            _user = user;
            _settings = settings;
            _menu = menu;
            _logger = logger;
        }

        // ═════════════════════════════ Admin ═════════════════════════════

        private static SelfOrderTableDto ToDto(SelfOrderTable t) => new(t.Id, t.Code, t.Label, t.IsActive, t.BranchId);
        private static string NormalizeCode(string? code) => (code ?? "").Trim().ToUpperInvariant();

        public async Task<List<SelfOrderTableDto>> ListTablesAsync(CancellationToken ct = default)
            => await _db.Set<SelfOrderTable>().AsNoTracking().OrderBy(t => t.Label).Select(t => ToDto(t)).ToListAsync(ct);

        private static string? ValidateTable(string code, SelfOrderTableUpsertDto dto)
        {
            if (code.Length == 0) return "Table code is required.";
            if (code.Length > 40) return "Table code is too long (max 40).";
            if (!code.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_')) return "Table code may only contain letters, digits, '-' and '_'.";
            if (string.IsNullOrWhiteSpace(dto.Label)) return "Label is required.";
            return null;
        }

        public async Task<BaseResponse<SelfOrderTableDto>> CreateTableAsync(SelfOrderTableUpsertDto dto, CancellationToken ct = default)
        {
            var code = NormalizeCode(dto.Code);
            var err = ValidateTable(code, dto);
            if (err != null) return new(false, "Validation", err);
            if (await _db.Set<SelfOrderTable>().AnyAsync(t => t.Code == code, ct))
                return new(false, "Validation", $"A table with code '{code}' already exists.");

            var t = new SelfOrderTable { Code = code, Label = dto.Label.Trim(), IsActive = dto.IsActive, BranchId = _user.BranchId };
            _db.Add(t);
            await _db.SaveChangesAsync(ct);
            return new(true, null, "Table created.", ToDto(t));
        }

        public async Task<BaseResponse<SelfOrderTableDto>> UpdateTableAsync(int id, SelfOrderTableUpsertDto dto, CancellationToken ct = default)
        {
            var code = NormalizeCode(dto.Code);
            var err = ValidateTable(code, dto);
            if (err != null) return new(false, "Validation", err);
            var t = await _db.Set<SelfOrderTable>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (t is null) return new(false, "NotFound", "Table not found.");
            if (await _db.Set<SelfOrderTable>().AnyAsync(x => x.Id != id && x.Code == code, ct))
                return new(false, "Validation", $"A table with code '{code}' already exists.");

            t.Code = code;
            t.Label = dto.Label.Trim();
            t.IsActive = dto.IsActive;
            await _db.SaveChangesAsync(ct);
            return new(true, null, "Table updated.", ToDto(t));
        }

        public async Task<bool> DeleteTableAsync(int id, CancellationToken ct = default)
        {
            var t = await _db.Set<SelfOrderTable>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (t is null) return false;
            _db.Remove(t);
            await _db.SaveChangesAsync(ct);
            return true;
        }

        public async Task<List<SelfOrderTableQrDto>> ListTableQrsAsync(string? publicBaseUrl, CancellationToken ct = default)
        {
            var baseUrl = publicBaseUrl;
            if (string.IsNullOrWhiteSpace(baseUrl))
                baseUrl = await _settings.GetRawAsync(PublicBaseUrlSettingKey, ct);
            baseUrl = (baseUrl ?? "").Trim().TrimEnd('/');

            var tables = await _db.Set<SelfOrderTable>().AsNoTracking().OrderBy(t => t.Label).ToListAsync(ct);
            return tables.Select(t =>
            {
                var url = $"{baseUrl}/order/{Uri.EscapeDataString(t.Code)}";
                return new SelfOrderTableQrDto(t.Id, t.Code, t.Label, t.IsActive, url, url);
            }).ToList();
        }

        // ════════════════════════════ Public ═════════════════════════════

        private async Task<SelfOrderTable?> FindActiveTableAsync(string tableCode, CancellationToken ct)
        {
            var code = NormalizeCode(tableCode);
            if (code.Length == 0) return null;
            var t = await _db.Set<SelfOrderTable>().AsNoTracking().FirstOrDefaultAsync(x => x.Code == code, ct);
            return t is { IsActive: true } ? t : null;
        }

        public async Task<BaseResponse<SelfOrderMenuDto>> GetMenuAsync(string tableCode, CancellationToken ct = default)
        {
            var t = await FindActiveTableAsync(tableCode, ct);
            if (t is null) return new(false, "NotFound", "This table is not available for self-ordering.");
            var cats = await _menu.BuildAsync(ct);
            return new(true, null, "OK", new SelfOrderMenuDto(t.Code, t.Label, cats));
        }

        public async Task<BaseResponse<SelfOrderSessionStartedDto>> StartSessionAsync(string tableCode, CancellationToken ct = default)
        {
            var t = await FindActiveTableAsync(tableCode, ct);
            if (t is null) return new(false, "NotFound", "This table is not available for self-ordering.");

            var s = new SelfOrderSession
            {
                TableCode = t.Code,
                Token = Guid.NewGuid(),
                Status = SelfOrderSessionStatuses.Open,
                CreatedOn = DateTime.UtcNow,
                LastActivityOn = DateTime.UtcNow,
                BranchId = t.BranchId,
            };
            _db.Add(s);
            await _db.SaveChangesAsync(ct);
            return new(true, null, "Session started.", new SelfOrderSessionStartedDto(s.Token, t.Code, t.Label, s.Status, s.CreatedOn));
        }

        public async Task<BaseResponse<SelfOrderSessionStatusDto>> SubmitAsync(Guid token, SelfOrderSubmitDto dto, CancellationToken ct = default)
        {
            var s = await _db.Set<SelfOrderSession>().FirstOrDefaultAsync(x => x.Token == token, ct);
            if (s is null) return new(false, "NotFound", "Session not found. Scan the QR code again.");
            if (s.Status == SelfOrderSessionStatuses.Closed) return new(false, "Closed", "This session is closed. Scan the QR code again to start a new order.");
            if (dto?.Items is null || dto.Items.Count == 0) return new(false, "Validation", "Pick at least one item.");
            if (dto.Items.Any(i => i.Quantity <= 0)) return new(false, "Validation", "Quantities must be greater than zero.");

            var table = await _db.Set<SelfOrderTable>().AsNoTracking().FirstOrDefaultAsync(t => t.Code == s.TableCode, ct);
            if (table is { IsActive: false }) return new(false, "NotFound", "This table is no longer available for self-ordering.");

            var note = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim();
            BaseResponse<TransactionDto> r;
            if (s.TransactionId is null)
            {
                var comment = $"Self-order table {s.TableCode}" + (note is null ? "" : $" | {note}");
                r = await _trx.CreateCoffeeShopOrder(null, 0, dto.Items, Actor, ct, comment: comment, isOpenInvoice: true);
                if (r.Success && r.Data != null) s.TransactionId = r.Data.Id;
            }
            else
            {
                r = await _trx.AddItemsToOpenInvoice(s.TransactionId.Value, dto.Items, Actor, ct);
            }

            if (!r.Success || r.Data is null)
            {
                _logger.LogWarning("Self-order submit failed for table {Table}: {Error} {Message}", s.TableCode, r.Error, r.Message);
                return new(false, r.Error ?? "OrderFailed", r.Message);
            }

            s.Status = SelfOrderSessionStatuses.Submitted;
            s.LastActivityOn = DateTime.UtcNow;
            if (note != null)
                s.GuestNote = string.IsNullOrWhiteSpace(s.GuestNote) ? note : $"{s.GuestNote} | {note}";
            await _db.SaveChangesAsync(ct);

            return new(true, null, "Order sent to the kitchen.", BuildStatus(s, r.Data));
        }

        public async Task<BaseResponse<SelfOrderSessionStatusDto>> GetStatusAsync(Guid token, CancellationToken ct = default)
        {
            var s = await _db.Set<SelfOrderSession>().FirstOrDefaultAsync(x => x.Token == token, ct);
            if (s is null) return new(false, "NotFound", "Session not found.");

            TransactionDto? trx = null;
            if (s.TransactionId is int tid)
            {
                trx = await _trx.GetWithItemsAsync(tid, ct);
                if (trx != null && trx.StatusId == 6 && s.Status != SelfOrderSessionStatuses.Closed)
                {
                    s.Status = SelfOrderSessionStatuses.Closed;
                    s.LastActivityOn = DateTime.UtcNow;
                    await _db.SaveChangesAsync(ct);
                }
            }
            return new(true, null, "OK", BuildStatus(s, trx));
        }

        private static SelfOrderSessionStatusDto BuildStatus(SelfOrderSession s, TransactionDto? trx)
        {
            var items = trx?.Items?
                .Select(i => new DeliveryOrderLineDto(i.ItemId, i.ItemName, i.Quantity, i.Price, i.IsIncluded ? 0m : i.Price * i.Quantity))
                .ToList() ?? new List<DeliveryOrderLineDto>();
            return new SelfOrderSessionStatusDto(
                s.Token, s.TableCode, s.Status, s.TransactionId,
                trx?.StatusId == 6, trx?.TotalPrice ?? 0m, items,
                s.GuestNote, s.CreatedOn, s.LastActivityOn);
        }
    }
}
