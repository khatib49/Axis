namespace Application.DTOs
{
    public record PaginatedResponse<T>(int TotalCount, IReadOnlyList<T> Data, int PageNumber, int PageSize,  decimal TotalInvoices = 0);

}
