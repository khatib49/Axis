namespace Application.DTOs
{
    /// <summary>A client's wallet as the UI sees it.</summary>
    public record WalletDto(
        int Id,
        int UserId,
        string? UserName,
        decimal Balance,
        bool IsActive,
        DateTime CreatedOn,
        DateTime? ModifiedOn
    );

    public record WalletTransactionDto(
        int Id,
        string Type,            // TopUp | Bonus | Spend | Refund | Adjustment
        decimal Amount,
        decimal BalanceAfter,
        string? Method,
        int? TransactionRecordId,
        string? Notes,
        string CreatedBy,
        DateTime CreatedOn
    );

    /// <summary>Cashier top-up. Method = Cash | Whish | Card (how the money arrived).</summary>
    public record WalletTopUpRequestDto(decimal Amount, string? Method, string? Notes);

    /// <summary>
    /// Outcome of a top-up — includes the applied bonus so the UI can show
    /// "Paid $100 → +$10 bonus → balance $135".
    /// </summary>
    public record WalletTopUpResultDto(
        int WalletId,
        decimal AmountPaid,
        decimal BonusGiven,
        decimal BonusPercent,
        decimal NewBalance
    );

    /// <summary>
    /// Admin-only. Positive Delta credits the wallet, negative debits it.
    /// CashOut=true means physical money left the drawer (a refund);
    /// false means a book correction with no cash movement.
    /// </summary>
    public record WalletAdjustRequestDto(decimal Delta, bool CashOut, string Reason);

    public record WalletBonusTierDto(int Id, decimal MinAmount, decimal BonusPercent, bool IsActive);
    public record WalletBonusTierUpsertDto(decimal MinAmount, decimal BonusPercent, bool IsActive = true);

    /// <summary>Balance + recent movements, one call for the clients screen.</summary>
    public record WalletSummaryDto(
        WalletDto Wallet,
        List<WalletTransactionDto> Recent
    );

    /// <summary>One row in the cross-wallet money feed (who, what, how, when).</summary>
    public record WalletMovementDto(
        int Id,
        DateTime CreatedOn,
        string Type,            // TopUp | Bonus | Spend | Refund | Adjustment | Deduction
        decimal Amount,
        string? Method,         // Cash | Whish | Card (top-ups/refunds)
        int UserId,
        string? UserName,
        string CreatedBy,
        string? Notes,
        int? TransactionRecordId,
        decimal BalanceAfter
    );

    /// <summary>
    /// Cash-box math over the filtered period. CashIn is what physically
    /// entered the drawer via top-ups; NetCashImpact = cash in − refunds out.
    /// Bonus and Spend never touch the drawer (bonus is gifted credit; spends
    /// were paid at top-up time).
    /// </summary>
    public record WalletMovementsSummaryDto(
        decimal CashIn,
        decimal WhishIn,
        decimal CardIn,
        decimal TotalTopUps,
        decimal BonusGiven,
        decimal Spent,
        decimal RefundedCashOut,
        decimal NetCashImpact,
        int TopUpCount
    );

    /// <summary>Feed page + period totals in one response.</summary>
    public record WalletMovementsPageDto(
        WalletMovementsSummaryDto Summary,
        int TotalCount,
        List<WalletMovementDto> Rows,
        int Page,
        int PageSize
    );
}
