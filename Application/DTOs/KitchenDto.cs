namespace Application.DTOs
{
    /// <summary>
    /// Kitchen order view for Chef role
    /// Shows FNB orders that need to be prepared
    /// </summary>
    public record KitchenOrderDto(
        int TransactionId,
        DateTime OrderTime,
        string OrderedBy,
        int FoodStatusId,
        string FoodStatusName,
        List<KitchenOrderItemDto> Items,
        string? CustomerName,
        int? RoomId,
        string? RoomName,
        int? SetId,
        string? SetName,
        string? Comment
    );

    /// <summary>
    /// Individual item in a kitchen order
    /// </summary>
    public record KitchenOrderItemDto(
        int ItemId,
        string ItemName,
        int Quantity,
        string? CategoryName,
        string? SpecialInstructions
    );

    /// <summary>
    /// Update food status request
    /// </summary>
    public record UpdateFoodStatusDto(
        int TransactionId,
        int NewFoodStatusId
    );

    /// <summary>
    /// Food status statistics for kitchen dashboard
    /// </summary>
    public record KitchenStatsDto(
        int PendingOrders,
        int InProgressOrders,
        int ReadyOrders,
        int ServedToday,
        decimal AveragePreparationTime
    );
}
