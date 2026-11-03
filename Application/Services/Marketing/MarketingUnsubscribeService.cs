using System.Security.Cryptography;
using System.Text;
using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Application.Services.Marketing
{
    /// <summary>
    /// Unsubscribe list + HMAC-signed tokens. Token = base64url(address) + "." + base64url(HMAC-SHA256(address, Jwt:Key)).
    /// Public base URL: IConfiguration "Marketing:PublicBaseUrl" (falls back to a relative /api path).
    /// </summary>
    public class MarketingUnsubscribeService : IMarketingUnsubscribeService
    {
        private readonly ApplicationDbContext _db;
        private readonly byte[] _key;
        private readonly string _baseUrl;

        public MarketingUnsubscribeService(ApplicationDbContext db, IConfiguration cfg)
        {
            _db = db;
            var key = cfg["Jwt:Key"];
            if (string.IsNullOrWhiteSpace(key)) key = "axis-marketing-fallback-key";
            _key = Encoding.UTF8.GetBytes(key);
            _baseUrl = (cfg["Marketing:PublicBaseUrl"] ?? "").TrimEnd('/');
        }

        private static string B64(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        private static byte[]? UnB64(string s)
        {
            try
            {
                var p = s.Replace('-', '+').Replace('_', '/');
                p = p.PadRight(p.Length + (4 - p.Length % 4) % 4, '=');
                return Convert.FromBase64String(p);
            }
            catch (FormatException) { return null; }
        }

        private byte[] Sign(string normalised) { using var h = new HMACSHA256(_key); return h.ComputeHash(Encoding.UTF8.GetBytes(normalised)); }

        public string CreateToken(string address)
        {
            var n = IMarketingUnsubscribeService.Normalise(address);
            return B64(Encoding.UTF8.GetBytes(n)) + "." + B64(Sign(n));
        }

        public string? ParseToken(string? token)
        {
            if (string.IsNullOrWhiteSpace(token)) return null;
            var parts = token.Trim().Split('.');
            if (parts.Length != 2) return null;
            var addrBytes = UnB64(parts[0]);
            var sig = UnB64(parts[1]);
            if (addrBytes is null || sig is null) return null;
            var address = Encoding.UTF8.GetString(addrBytes);
            if (string.IsNullOrWhiteSpace(address)) return null;
            var expected = Sign(IMarketingUnsubscribeService.Normalise(address));
            return CryptographicOperations.FixedTimeEquals(expected, sig) ? address : null;
        }

        public string BuildUnsubscribeUrl(string address) =>
            $"{_baseUrl}/api/marketing/unsubscribe?token={Uri.EscapeDataString(CreateToken(address))}";

        public async Task<bool> UnsubscribeByTokenAsync(string? token, CancellationToken ct = default)
        {
            var address = ParseToken(token);
            if (address is null) return false;
            await AddAsync(address, "Link", ct);
            return true;
        }

        public async Task<BaseResponse<UnsubscribeDto>> AddAsync(string address, string source, CancellationToken ct = default)
        {
            var n = IMarketingUnsubscribeService.Normalise(address);
            if (n.Length == 0) return new(false, "Address is required.", "Validation failed");
            var existing = await _db.Set<MktUnsubscribe>().FirstOrDefaultAsync(u => u.Address == n, ct);
            if (existing is not null) return new(true, null, "Already unsubscribed", ToDto(existing));
            var e = new MktUnsubscribe { Address = n, Channel = n.Contains('@') ? "Email" : "Phone", On = DateTime.UtcNow, Source = source };
            _db.Add(e);
            await _db.SaveChangesAsync(ct);
            return new(true, null, "Unsubscribed", ToDto(e));
        }

        public async Task<bool> RemoveAsync(int id, CancellationToken ct = default)
        {
            var e = await _db.Set<MktUnsubscribe>().FirstOrDefaultAsync(u => u.Id == id, ct);
            if (e is null) return false;
            _db.Remove(e);
            await _db.SaveChangesAsync(ct);
            return true;
        }

        public async Task<IReadOnlyList<UnsubscribeDto>> ListAsync(CancellationToken ct = default) =>
            (await _db.Set<MktUnsubscribe>().AsNoTracking().OrderByDescending(u => u.On).ToListAsync(ct)).Select(ToDto).ToList();

        public async Task<bool> IsUnsubscribedAsync(string address, CancellationToken ct = default)
        {
            var n = IMarketingUnsubscribeService.Normalise(address);
            return await _db.Set<MktUnsubscribe>().AsNoTracking().AnyAsync(u => u.Address == n, ct);
        }

        public async Task<HashSet<string>> AllAddressesAsync(CancellationToken ct = default) =>
            (await _db.Set<MktUnsubscribe>().AsNoTracking().Select(u => u.Address).ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        private static UnsubscribeDto ToDto(MktUnsubscribe u) => new(u.Id, u.Address, u.Channel, u.On, u.Source);
    }
}
