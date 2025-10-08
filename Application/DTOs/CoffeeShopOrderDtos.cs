namespace Application.DTOs
{
    public record CoffeeShopOrderDto(int Id, int UserId, int CardId, int ItemId, int Quantity, decimal Price, DateTime Timestamp);
    public record CoffeeShopOrderCreateDto(int UserId, int CardId, int ItemId, int Quantity, decimal Price);
    public record CoffeeShopOrderUpdateDto(int? Quantity, decimal? Price);
}
