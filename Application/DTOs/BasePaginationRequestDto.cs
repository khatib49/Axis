namespace Application.DTOs
{
    public record BasePaginationRequestDto(int Page = 1, int PageSize = 10 , int? CategoryId = null , string? search = null , string? createdBy = null);

}
