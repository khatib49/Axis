namespace Application.DTOs
{
    /// <summary>
    /// Represents profit calculation for a single business segment (FNB, Gaming, or TCG Retail)
    /// </summary>
    public record ProfitDto(
        decimal TotalRevenue,
        decimal TotalExpenses,
        decimal NetProfit,
        decimal ProfitMargin,
        int TransactionCount,
        DateTime? FromDate,
        DateTime? ToDate
    );

    /// <summary>
    /// Represents overall business profit with detailed breakdown (FNB + Gaming + TCG Retail)
    /// </summary>
    public record DetailedOverallProfitDto(
        ProfitDto FnbProfit,
        ProfitDto GamingProfit,
        ProfitDto TcgRetailProfit,
        decimal TotalRevenue,
        decimal TotalExpenses,
        decimal NetProfit,
        decimal OverallProfitMargin,
        DateTime? FromDate,
        DateTime? ToDate
    );
}
