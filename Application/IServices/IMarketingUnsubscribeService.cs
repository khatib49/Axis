using Application.DTOs;

namespace Application.IServices
{
    public interface IMarketingUnsubscribeService
    {
        /// <summary>HMAC token for an address (used in e-mail unsubscribe links).</summary>
        string CreateToken(string address);
        /// <summary>Address encoded in a valid token, or null when the signature does not match.</summary>
        string? ParseToken(string? token);
        /// <summary>Absolute unsubscribe URL for an address.</summary>
        string BuildUnsubscribeUrl(string address);
        /// <summary>Records the unsubscribe (idempotent). Returns false for a bad token.</summary>
        Task<bool> UnsubscribeByTokenAsync(string? token, CancellationToken ct = default);
        Task<BaseResponse<UnsubscribeDto>> AddAsync(string address, string source, CancellationToken ct = default);
        Task<bool> RemoveAsync(int id, CancellationToken ct = default);
        Task<IReadOnlyList<UnsubscribeDto>> ListAsync(CancellationToken ct = default);
        Task<bool> IsUnsubscribedAsync(string address, CancellationToken ct = default);
        /// <summary>Normalised set of all unsubscribed addresses (for bulk skipping at send time).</summary>
        Task<HashSet<string>> AllAddressesAsync(CancellationToken ct = default);
        static string Normalise(string address) => (address ?? "").Trim().ToLowerInvariant();
    }
}
