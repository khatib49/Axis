namespace Application.DTOs
{
    public record ExpenseCreateDto(
        int CategoryId,
        decimal Amount,
        string? PaymentMethod,
        string? Comment,
        DateTime FromDate,
        DateTime ToDate
    );

    public record ExpenseUpdateDto(
        decimal Amount,
        string? PaymentMethod,
        string? Comment,
        DateTime FromDate,
        DateTime ToDate,
        int CategoryId
    );

    public record ExpenseDto(
        int Id,
        int CategoryId,
        string CategoryName,
        decimal Amount,
        string? PaymentMethod,
        string? Comment,
        DateTime FromDate,
        DateTime ToDate,
        int? CreatedBy,
        DateTime CreatedOn
    );

    public record ExpenseCategoryUpdateDto(string Name, string? Description);
    public record ExpenseFilter(
        DateTime? From = null,
        DateTime? To = null,
        int? CategoryId = null,
        int Page = 1,
        int PageSize = 20
    );

    public record PagedExpensesResult(
        int Page,
        int PageSize,
        int TotalCount,
        decimal TotalAmount,                // sum of page items
        decimal TotalAmountAll,             // sum of all filtered items (ignore paging)
        IReadOnlyList<ExpenseDto> Items
    );
    public record ExpenseCategoryCreateDto(string Name, string? Description);
    public record ExpenseCategoryDto(int Id, string Name, string? Description);


}
