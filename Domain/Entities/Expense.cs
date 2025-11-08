namespace Domain.Entities
{
    public class Expense
    {
        public int Id { get; set; }
        public int FK_CategoryId { get; set; }
        public ExpenseCategory Category { get; set; } = default!;
        public decimal Amount { get; set; }
        public string? PaymentMethod { get; set; }
        public string? Comment { get; set; }
        public DateTime FromDate { get; set; }  
        public DateTime ToDate { get; set; } 
        public int? CreatedBy { get; set; } 
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
    }
}
