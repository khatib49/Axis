namespace Application.DTOs
{
    public record ScheduledReportDto(
        int Id,
        string Name,
        string ReportType,
        string Frequency,
        int RunAtHourUtc,
        int? DayOfWeek,
        string Recipients,
        string Channel,
        string Format,
        bool IsActive,
        DateTime? LastRunOn,
        string? LastStatus,
        string CreatedBy,
        DateTime CreatedOn,
        int? BranchId);

    public record ScheduledReportCreateDto(
        string Name,
        string ReportType,
        string Frequency,
        int RunAtHourUtc,
        string Recipients,
        string Channel = "Email",
        string Format = "Html",
        int? DayOfWeek = null,
        bool IsActive = true,
        int? BranchId = null);

    public record ScheduledReportUpdateDto(
        string? Name = null,
        string? ReportType = null,
        string? Frequency = null,
        int? RunAtHourUtc = null,
        int? DayOfWeek = null,
        string? Recipients = null,
        string? Channel = null,
        string? Format = null,
        bool? IsActive = null,
        int? BranchId = null);

    public record ReportRunDto(
        int Id,
        int ScheduledReportId,
        DateTime RanOn,
        string Status,
        string? Error,
        string? PayloadPreview,
        bool IsManual);

    /// <summary>A rendered-agnostic report: a title, summary lines and a table.</summary>
    public record ReportPayload(
        string Title,
        string ReportType,
        DateTime GeneratedOn,
        IReadOnlyList<KeyValuePair<string, string>> Summary,
        IReadOnlyList<string> Columns,
        IReadOnlyList<IReadOnlyList<string>> Rows);

    /// <summary>Built + rendered report returned by preview / run-now.</summary>
    public record ReportPreviewDto(string Title, string ReportType, string Format, string Content, int RowCount);

    public record OwnerPeriodDto(decimal Sales, int Orders, decimal AverageTicket);
    public record OwnerTopItemDto(int ItemId, string ItemName, string CategoryName, int Quantity, decimal Amount);
    public record OwnerSeriesPointDto(DateTime Date, decimal Total, decimal Items, decimal Games, decimal Events);

    public record OwnerSummaryDto(
        DateTime GeneratedOn,
        DateTime Day,
        OwnerPeriodDto Today,
        OwnerPeriodDto Yesterday,
        OwnerPeriodDto SameDayLastWeek,
        decimal? SalesDeltaVsYesterdayPct,
        decimal? SalesDeltaVsLastWeekPct,
        decimal? OrdersDeltaVsYesterdayPct,
        decimal? OrdersDeltaVsLastWeekPct,
        int OpenInvoicesCount,
        decimal OpenInvoicesValue,
        int LowStockCount,
        IReadOnlyList<OwnerTopItemDto> TopItems,
        IReadOnlyList<OwnerSeriesPointDto> Last14Days);
}
