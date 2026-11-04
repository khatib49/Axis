namespace Application.DTOs
{
    // ───────────────────────── Promotion accounting (phase 3) ────────────────
    /// <summary>Outcome of one posting attempt. Success + JournalEntryId when booked; Pending when the business action stands but the ledger line is deferred.</summary>
    public record PromotionPostingResultDto(
        int PostingId, string ReferenceType, int ReferenceId, int Sequence, decimal Amount,
        string Status, int? JournalEntryId, string? EntryNumber, string? Error, bool AlreadyPosted = false);

    public record PromotionPostingDto(
        int Id, string ReferenceType, string Kind, int ReferenceId, int Sequence, decimal Amount,
        int? InvoiceId, int? GiftCardId, int? JournalEntryId, string? EntryNumber, string Status,
        string? Error, string? Description, DateTime On, DateTime? PostedOn, int Attempts, string By,
        string? DebitAccount, string? CreditAccount);

    public record PromotionAccountsDto(
        int LiabilityAccountId, string LiabilityAccountNumber, string LiabilityAccountName,
        int DiscountAccountId, string DiscountAccountNumber, string DiscountAccountName,
        int CashAccountId, string CashAccountNumber,
        int SalesAccountId, string SalesAccountNumber,
        bool CreatedLiability, bool CreatedDiscount);

    /// <summary>Outstanding gift card value (what the venue still owes card holders) against the 2150 ledger balance.</summary>
    public record GiftCardLiabilityReportDto(
        DateTime AsOf,
        int ActiveCards,
        decimal ActiveCardBalance,
        int ExpiredCards,
        decimal ExpiredCardBalance,
        decimal LedgerBalance,
        decimal Difference,
        int PendingPostings,
        decimal PendingAmount,
        string LiabilityAccountNumber,
        string? Note);

    public record PromotionDiscountKindDto(
        string Kind,
        int Postings,
        int Applied,
        int Refunded,
        decimal DiscountGiven,
        decimal DiscountRefunded,
        decimal NetDiscount,
        decimal PostedAmount,
        decimal PendingAmount,
        int PendingPostings);

    public record PromotionDiscountsReportDto(
        DateTime From, DateTime To, List<PromotionDiscountKindDto> Kinds,
        decimal TotalNetDiscount, decimal TotalPosted, decimal TotalPending,
        decimal GiftCardRedeemed, string DiscountAccountNumber);

    public record PromotionRepostResultDto(int Attempted, int Posted, int StillPending, List<PromotionPostingResultDto> Results);
}
