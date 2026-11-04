namespace Application.DTOs
{
    public record BranchDto(
        int Id,
        string Code,
        string Name,
        string? Address,
        string? Phone,
        string CurrencyCode,
        string TimeZoneId,
        bool IsActive,
        bool IsDefault,
        string? OpeningTime,   // "HH:mm" or null
        string? ClosingTime,
        DateTime CreatedOn,
        DateTime? ModifiedOn);

    public record BranchCreateDto(
        string Code,
        string Name,
        string? Address = null,
        string? Phone = null,
        string? CurrencyCode = "USD",
        string? TimeZoneId = "Asia/Beirut",
        bool IsActive = true,
        bool IsDefault = false,
        string? OpeningTime = null,
        string? ClosingTime = null);

    public record BranchUpdateDto(
        string? Code = null,
        string? Name = null,
        string? Address = null,
        string? Phone = null,
        string? CurrencyCode = null,
        string? TimeZoneId = null,
        bool? IsActive = null,
        bool? IsDefault = null,
        string? OpeningTime = null,
        string? ClosingTime = null);

    /// <summary>Branches the caller may work in. Source: "admin" | "assigned" | "default-fallback".</summary>
    public record MyBranchesDto(IReadOnlyList<BranchDto> Branches, int? DefaultBranchId, string Source);

    public record UserBranchDto(int Id, int UserId, string? UserName, string? Email, int BranchId, string BranchCode, string BranchName, bool IsDefault);
    public record AssignUserRequest(int UserId, bool IsDefault = false);

    public record BranchSettingDto(string Key, string? Value, DateTime UpdatedOn, string? UpdatedBy);
    public record BranchSettingUpsertDto(string Key, string? Value);

    /// <summary>
    /// Per-branch KPIs. Attribution: "transaction" once at least one transaction
    /// carries a BranchId (legacy rows without one still count towards the default
    /// branch), "default-branch" while nothing has been stamped yet.
    /// </summary>
    public record BranchKpiDto(
        int BranchId,
        string Code,
        string Name,
        string CurrencyCode,
        bool IsDefault,
        decimal Sales,
        int Orders,
        decimal AverageTicket,
        int OpenInvoices,
        decimal OpenInvoicesValue);

    public record ConsolidatedDashboardDto(
        DateTime From,
        DateTime To,
        string Attribution,
        string AttributionNote,
        IReadOnlyList<BranchKpiDto> Branches,
        BranchKpiDto Total);

    /// <summary>
    /// Result of POST api/branches/backfill: rows stamped per table (table name =>
    /// number of rows whose BranchId was null and is now BranchId).
    /// </summary>
    public record BranchBackfillResultDto(int BranchId, string BranchCode, string BranchName, IReadOnlyList<string> Tables, Dictionary<string, int> Counts, int Total);
}
