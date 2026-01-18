namespace Application.DTOs
{
    public record KitchenBarOrderDto(
       int Id,
       int TransactionId,
       int ItemId,
       string ItemName,
       int Quantity,
       decimal ItemPrice,
       string Station, // Kitchen or Bar
       string Status, // Pending, Preparing, Done
       DateTime OrderedAt,
       DateTime? PreparedAt,
       int? PreparedBy,
       string? PreparedByUsername,
       DateTime? PrintedAt,
       string? TableNumber,
       string? GuestName,
       string? ItemComment,
       string CreatedByUsername,
       DateTime CreatedAt
   );

    // List/Filter DTO
    public record KitchenBarOrderListDto(
        string? Station = null, // Filter: Kitchen, Bar, or null for all
        string? Status = null,  // Filter: Pending, Preparing, Done, or null for all
        DateTime? FromDate = null,
        DateTime? ToDate = null,
        int Page = 1,
        int PageSize = 50
    );

    // Update status DTO
    public record KitchenBarOrderUpdateStatusDto(
        int Id,
        string Status, // Preparing or Done
        int? PreparedBy = null
    );

    // Mark as printed DTO
    public record KitchenBarOrderMarkPrintedDto(
        List<int> OrderIds
    );
}
