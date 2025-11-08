namespace Domain.Entities
{

    public class ExpenseCategory
    {
        public int Id { get; set; }
        public string Name { get; set; } = default!;
        public string? Description { get; set; }
        public ICollection<Expense> Expenses { get; set; } = new List<Expense>();
    }
}
