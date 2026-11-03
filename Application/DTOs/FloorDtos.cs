namespace Application.DTOs
{
    // ── Areas & tables ───────────────────────────────────────────────
    public record FloorAreaDto(int Id, string Name, int SortOrder, bool IsActive, int? BranchId, int TableCount);
    public record FloorAreaUpsertDto(string Name, int SortOrder = 0, bool IsActive = true);

    public record DiningTableDto(int Id, int AreaId, string AreaName, string Code, string Name, int Capacity, string Shape,
        int PosX, int PosY, int Width, int Height, bool IsActive, int? BranchId);
    public record DiningTableUpsertDto(int AreaId, string Code, string Name, int Capacity = 4, string Shape = "Rect",
        int PosX = 0, int PosY = 0, int Width = 2, int Height = 2, bool IsActive = true);
    public record TablePositionDto(int Id, int PosX, int PosY, int Width, int Height);
    public record BulkLayoutSaveDto(List<TablePositionDto> Positions);

    // ── Sessions / layout ────────────────────────────────────────────
    public record OpenSessionSummaryDto(int SessionId, int TransactionId, decimal Total, int GuestCount, DateTime OpenedOn,
        int OpenedMinutes, int ItemsCount, string OpenedBy, string? Notes);
    public record LayoutTableDto(int Id, int AreaId, string Code, string Name, int Capacity, string Shape,
        int PosX, int PosY, int Width, int Height, bool IsActive, OpenSessionSummaryDto? OpenSession);
    public record LayoutAreaDto(int Id, string Name, int SortOrder, List<LayoutTableDto> Tables);
    public record FloorLayoutDto(List<LayoutAreaDto> Areas, DateTime GeneratedOn);

    public record TableSessionDto(int Id, int TableId, string TableCode, string TableName, int TransactionId, int GuestCount,
        DateTime OpenedOn, DateTime? ClosedOn, string OpenedBy, string Status, string? Notes);
    public record OpenTableRequestDto(int TableId, int GuestCount = 1, int? UserId = null, string? Notes = null,
        List<OrderItemRequest>? Items = null);
    public record SeatInvoiceRequestDto(int TableId, int TransactionId, int GuestCount = 1, string? Notes = null);
    public record TransferSessionRequestDto(int TargetTableId);
    public record MergeInvoicesRequestDto(int SourceTransactionId, int TargetTransactionId);
    public record MergeResultDto(int TargetTransactionId, int SourceTransactionId, decimal TargetTotal, int MovedLines);

    // ── Courses ──────────────────────────────────────────────────────
    public record OrderCourseDto(int Id, int TransactionId, int ItemId, string ItemName, int Quantity, int CourseNumber,
        DateTime? FiredOn, string? FiredBy);
    public record CourseAssignmentDto(int ItemId, int CourseNumber);
    public record SetCoursesRequestDto(List<CourseAssignmentDto> Assignments);

    // ── Bill splits ──────────────────────────────────────────────────
    public record BillSplitDto(int Id, int TransactionId, int SplitNumber, decimal Amount, string Method, DateTime? PaidOn,
        string? PaidBy, string? Note);
    public record BillSplitListDto(int TransactionId, decimal TotalPrice, decimal SplitTotal, decimal PaidTotal, decimal Remaining,
        List<BillSplitDto> Splits);
    public record EqualSplitRequestDto(int Ways);
    public record CustomSplitLineDto(decimal Amount, string? Method = null, string? Note = null);
    public record CustomSplitRequestDto(List<CustomSplitLineDto> Splits);
    public record PaySplitRequestDto(string Method = "Cash", string? Note = null);

    // ── Tips ─────────────────────────────────────────────────────────
    public record TipDto(int Id, int TransactionId, decimal Amount, string Method, int? StaffUserId, string? StaffName,
        DateTime CreatedOn, string CreatedBy);
    public record TipCreateDto(decimal Amount, string Method = "Cash", int? StaffUserId = null, string? StaffName = null);
    public record StaffTipsReportRowDto(int? StaffUserId, string StaffName, int Count, decimal Total);
    public record DailyTipsReportDto(DateTime Day, decimal Total, int Count, List<StaffTipsReportRowDto> ByStaff);

    // ── Reservations ─────────────────────────────────────────────────
    public record ReservationDto(int Id, string CustomerName, string Phone, int? UserId, string ResourceType, int? TableId,
        string? TableCode, int? RoomId, int? GameId, string? ResourceName, DateTime StartOn, DateTime EndOn, int PartySize, string Status,
        string? Notes, string Source, DateTime CreatedOn, string CreatedBy, DateTime? ConfirmedOn, int? SeatedTransactionId,
        int? BranchId);
    public record ReservationUpsertDto(string CustomerName, string Phone, string ResourceType, DateTime StartOn, DateTime EndOn,
        int PartySize = 1, int? TableId = null, int? RoomId = null, int? GameId = null, int? UserId = null, string? Notes = null,
        string Source = "Phone", bool ConfirmNow = false);
    public record PublicReservationRequestDto(string CustomerName, string Phone, string ResourceType, DateTime StartOn, DateTime EndOn,
        int PartySize = 1, int? TableId = null, int? RoomId = null, int? GameId = null, string? Notes = null);
    public record ReservationListFilterDto(DateTime? From = null, DateTime? To = null, string? Status = null,
        string? ResourceType = null, string? Search = null, int Page = 1, int PageSize = 50);
    public record AvailabilityQueryDto(string ResourceType, DateTime StartOn, DateTime EndOn, int? TableId = null,
        int? RoomId = null, int? GameId = null, int? ExcludeReservationId = null);
    public record AvailabilityResultDto(bool Available, List<ReservationDto> Conflicts);
    public record SeatReservationRequestDto(bool OpenTableSession = false, int? GuestCount = null);
    public record ReservationBoardDto(DateTime Day, int Pending, int Confirmed, int Seated, int NoShow, int Cancelled,
        List<ReservationDto> Reservations);
}
