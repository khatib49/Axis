namespace Application.DTOs
{
    public record CoffeeShopOrderDto(Guid Id, Guid UserId, Guid CardId, Guid ItemId, int Quantity, decimal Price, DateTime Timestamp);
    public record CoffeeShopOrderCreateDto(Guid UserId, Guid CardId, Guid ItemId, int Quantity, decimal Price);
    public record CoffeeShopOrderUpdateDto(int? Quantity, decimal? Price);
}
