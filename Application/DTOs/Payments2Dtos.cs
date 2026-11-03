namespace Application.DTOs
{
    // ───────────────────────── Whish invoice payments ─────────────────────────
    /// <summary>Till / staff start. ReturnUrl: UI page to bounce back to after the hosted checkout (optional).</summary>
    public record WhishStartDto(int TransactionId, string Source = "Till", string? ReturnUrl = null);
    /// <summary>Guest start: Token is the self-order session token (GUID) or the kiosk device token.</summary>
    public record WhishStartPublicDto(string Token, int TransactionId, string? ReturnUrl = null);
    public record WhishStartResultDto(int RequestId, string CollectUrl, string QrPayload, decimal Amount, string Currency, string Status);
    public record WhishStatusDto(int RequestId, int TransactionId, string Status, decimal Amount, string Currency, bool InvoiceClosed, DateTime? PaidOn, string? CollectUrl);

    // ───────────────────────── Customer display ─────────────────────────
    public record DisplayCartLineDto(string Name, decimal Qty, decimal Price);
    public record DisplayPushDto(int? TransactionId, List<DisplayCartLineDto>? Cart, string? Status);
    public record DisplayCreateDto(string RegisterName);
    public record DisplayRequestWhishDto(int? TransactionId = null, string? ReturnUrl = null);
    /// <summary>Admin row: includes the screen token.</summary>
    public record DisplayStateDto(
        int Id, string RegisterName, string Token, int? TransactionId, string Status,
        decimal TotalUsd, decimal RateLbp, decimal TotalLbp, int? WhishRequestId, DateTime UpdatedOn);
    /// <summary>What the public screen renders (no token echoed back).</summary>
    public record DisplayViewDto(
        string RegisterName, string Status, int? TransactionId, List<DisplayCartLineDto> Cart,
        decimal TotalUsd, decimal RateLbp, decimal TotalLbp, string? QrPayload, decimal? WhishAmount, DateTime UpdatedOn);
}
