namespace Application.DTOs
{
    /// <param name="StatusId">Items list only: 1 Enabled (default when null), 2 Disabled, 3 Deleted, 0 = all statuses.</param>
    public record BasePaginationRequestDto(int Page = 1, int PageSize = 10 , int? CategoryId = null , string? search = null , string? createdBy = null, int? StatusId = null);

}
