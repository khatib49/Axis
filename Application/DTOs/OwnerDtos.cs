namespace Application.DTOs
{
    // ── Owners ──────────────────────────────────────────────────────────

    public record OwnerDto(
        int Id,
        string Name,
        decimal OwnershipPercent,
        int DrawingsAccountId,
        string DrawingsAccountNumber,
        string DrawingsAccountName,
        string? Notes,
        bool IsActive,
        DateTime CreatedOn,
        DateTime? ModifiedOn
    );

    /// <summary>
    /// ExistingAccountId: link an Equity account that already exists (e.g. an
    /// old "Omar drawings" account) instead of creating a new one. It is moved
    /// under the Owners' Drawings header so the header rolls it up.
    /// </summary>
    public record OwnerCreateDto(
        string Name,
        decimal OwnershipPercent,
        string? Notes,
        int? ExistingAccountId
    );

    public record OwnerUpdateDto(
        string Name,
        decimal OwnershipPercent,
        string? Notes,
        bool IsActive
    );

    // ── Drawings ────────────────────────────────────────────────────────

    public record OwnerDrawingDto(
        int Id,
        int OwnerId,
        string OwnerName,
        decimal Amount,
        DateTime DrawingDate,
        string? PaymentMethod,
        string? Comment,
        int? JournalEntryId,
        string? JournalEntryNumber,
        bool IsVoided,
        DateTime? VoidedOn,
        string? VoidReason,
        int? CreatedBy,
        DateTime CreatedOn
    );

    public record OwnerDrawingCreateDto(
        int OwnerId,
        decimal Amount,
        DateTime DrawingDate,
        string? PaymentMethod,
        string? Comment
    );

    public record OwnerDrawingUpdateDto(
        int OwnerId,
        decimal Amount,
        DateTime DrawingDate,
        string? PaymentMethod,
        string? Comment
    );

    public record OwnerDrawingVoidDto(string? Reason);

    public record OwnerDrawingFilter(
        DateTime? From,
        DateTime? To,
        int? OwnerId,
        bool IncludeVoided = false,
        int Page = 1,
        int PageSize = 50
    );

    public record PagedOwnerDrawingsResult(
        int Page,
        int PageSize,
        int TotalCount,
        decimal TotalAmountAll,        // non-voided rows matching the filter
        IReadOnlyList<OwnerDrawingDto> Items
    );

    // ── Summary report ──────────────────────────────────────────────────

    /// <summary>
    /// Per-owner drawings for a period, read from the ledger (posted, non-voided
    /// journal lines on each owner's drawings account), so every route into
    /// the account counts: this page, an entry category mapped to it, or a
    /// manual journal entry.
    /// </summary>
    public record OwnerDrawingsSummaryDto(
        DateTime? From,
        DateTime? To,
        int HeaderAccountId,
        string HeaderAccountNumber,
        string HeaderAccountName,
        decimal TotalDrawings,              // period, = header rollup
        decimal LifetimeTotalDrawings,      // all time
        decimal TotalOwnershipPercent,      // active owners; should be 100
        List<OwnerDrawingsLineDto> Owners,
        // Sub-accounts under the header that no owner is linked to. Shown so
        // the header total always equals the sum of its rows.
        List<OwnerDrawingsLineDto> OtherAccounts,
        // Entry categories still mapped to an Equity account outside the
        // header (the old "Omar cash out" workaround). Remap them to the
        // owner's drawings account so they count here.
        List<UnlinkedEquityCategoryDto> UnlinkedEquityCategories
    );

    public record OwnerDrawingsLineDto(
        int? OwnerId,
        string Name,
        decimal OwnershipPercent,
        int AccountId,
        string AccountNumber,
        string AccountName,
        bool IsActive,
        decimal Drawn,                 // period
        int EntryCount,                // period
        decimal ShareOfDrawingsPercent,// Drawn ÷ TotalDrawings × 100
        decimal EntitledAmount,        // TotalDrawings × OwnershipPercent ÷ 100
        decimal Variance,              // Drawn − Entitled (+ = over-drawn)
        decimal LifetimeDrawn
    );

    public record UnlinkedEquityCategoryDto(
        int CategoryId,
        string CategoryName,
        string AccountNumber,
        string AccountName,
        decimal TotalAmount,
        int EntryCount
    );
}
