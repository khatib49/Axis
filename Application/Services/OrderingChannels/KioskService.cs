using Application.Common;
using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Services.OrderingChannels
{
    /// <summary>Kiosk devices + anonymous takeaway ordering authenticated by X-Kiosk-Token.</summary>
    public class KioskService : IKioskService
    {
        public const string Source = "kiosk";

        private readonly ApplicationDbContext _db;
        private readonly IDeliveryService _delivery;
        private readonly PublicMenuBuilder _menu;
        private readonly ICurrentUserAccessor _user;
        private readonly ILogger<KioskService> _logger;

        public KioskService(ApplicationDbContext db, IDeliveryService delivery, PublicMenuBuilder menu, ICurrentUserAccessor user, ILogger<KioskService> logger)
        {
            _db = db;
            _delivery = delivery;
            _menu = menu;
            _user = user;
            _logger = logger;
        }

        private static KioskDeviceDto ToDto(KioskDevice d) => new(d.Id, d.Name, d.Token, d.IsActive, d.LastSeenOn, d.BranchId);
        private static string NewToken() => Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N")[..16];

        public async Task<List<KioskDeviceDto>> ListDevicesAsync(CancellationToken ct = default)
            => await _db.Set<KioskDevice>().AsNoTracking().OrderBy(d => d.Name).Select(d => ToDto(d)).ToListAsync(ct);

        public async Task<BaseResponse<KioskDeviceDto>> CreateDeviceAsync(KioskDeviceUpsertDto dto, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(dto.Name)) return new(false, "Validation", "Device name is required.");
            var d = new KioskDevice { Name = dto.Name.Trim(), Token = NewToken(), IsActive = dto.IsActive, BranchId = _user.BranchId };
            _db.Add(d);
            await _db.SaveChangesAsync(ct);
            return new(true, null, "Device created.", ToDto(d));
        }

        public async Task<BaseResponse<KioskDeviceDto>> UpdateDeviceAsync(int id, KioskDeviceUpsertDto dto, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(dto.Name)) return new(false, "Validation", "Device name is required.");
            var d = await _db.Set<KioskDevice>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (d is null) return new(false, "NotFound", "Device not found.");
            d.Name = dto.Name.Trim();
            d.IsActive = dto.IsActive;
            await _db.SaveChangesAsync(ct);
            return new(true, null, "Device updated.", ToDto(d));
        }

        public async Task<BaseResponse<KioskDeviceDto>> RotateTokenAsync(int id, CancellationToken ct = default)
        {
            var d = await _db.Set<KioskDevice>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (d is null) return new(false, "NotFound", "Device not found.");
            d.Token = NewToken();
            await _db.SaveChangesAsync(ct);
            return new(true, null, "Token rotated.", ToDto(d));
        }

        public async Task<bool> DeleteDeviceAsync(int id, CancellationToken ct = default)
        {
            var d = await _db.Set<KioskDevice>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (d is null) return false;
            _db.Remove(d);
            await _db.SaveChangesAsync(ct);
            return true;
        }

        private async Task<KioskDevice?> AuthenticateAsync(string? token, CancellationToken ct)
        {
            var t = (token ?? "").Trim();
            if (t.Length == 0) return null;
            var d = await _db.Set<KioskDevice>().FirstOrDefaultAsync(x => x.Token == t, ct);
            if (d is null || !d.IsActive) return null;
            d.LastSeenOn = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            return d;
        }

        public async Task<BaseResponse<SelfOrderMenuDto>> GetMenuAsync(string? token, CancellationToken ct = default)
        {
            var d = await AuthenticateAsync(token, ct);
            if (d is null) return new(false, "Unauthorized", "Unknown or inactive kiosk.");
            var cats = await _menu.BuildAsync(ct);
            return new(true, null, "OK", new SelfOrderMenuDto(Source, d.Name, cats));
        }

        public async Task<BaseResponse<KioskOrderResultDto>> PlaceOrderAsync(string? token, KioskOrderRequest req, CancellationToken ct = default)
        {
            var d = await AuthenticateAsync(token, ct);
            if (d is null) return new(false, "Unauthorized", "Unknown or inactive kiosk.");
            if (req?.Items is null || req.Items.Count == 0) return new(false, "Validation", "Pick at least one item.");

            var create = new DeliveryOrderCreateDto(
                Type: DeliveryOrderTypes.Takeaway,
                CustomerName: string.IsNullOrWhiteSpace(req.CustomerName) ? "Kiosk guest" : req.CustomerName.Trim(),
                Phone: null,
                Address: null,
                Notes: string.IsNullOrWhiteSpace(req.Note) ? $"Kiosk {d.Name}" : $"Kiosk {d.Name}: {req.Note.Trim()}",
                Items: req.Items);

            var r = await _delivery.CreateFromSourceAsync(create, $"kiosk:{d.Name}", Source, null, null, ct);
            if (!r.Success || r.Data is null) return new(false, r.Error ?? "OrderFailed", r.Message);

            _logger.LogInformation("Kiosk {Device} placed order #{OrderId}", d.Name, r.Data.Id);
            return new(true, null, "Order placed.", new KioskOrderResultDto(r.Data.Id, r.Data.TransactionId, r.Data.Total, d.Name, r.Data.CreatedOn));
        }
    }
}
