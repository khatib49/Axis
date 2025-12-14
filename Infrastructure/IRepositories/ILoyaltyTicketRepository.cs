using Domain.Models;

namespace Infrastructure.IRepositories
{

    public interface ILoyaltyTicketRepository
    {
        Task<LoyaltyTicket?> GetByIdAsync(int ticketId);
        Task<LoyaltyTicket?> GetByTransactionIdAsync(int transactionId);
        Task<List<LoyaltyTicket>> GetByCustomerPhoneAsync(string phoneNumber);
        Task<List<LoyaltyTicket>> GetByDrawMonthAsync(string drawMonth);
        Task<List<LoyaltyTicket>> GetValidTicketsByDrawMonthAsync(string drawMonth);
        Task<List<LoyaltyTicket>> GetByCustomerAndMonthAsync(string phoneNumber, string drawMonth);
        Task<List<LoyaltyTicket>> GetByDateRangeAsync(DateTime startDate, DateTime endDate);
        Task<List<LoyaltyTicket>> GetExpiredTicketsAsync(string currentMonth);
        Task<LoyaltyTicket> CreateAsync(LoyaltyTicket ticket);
        Task<LoyaltyTicket> UpdateAsync(LoyaltyTicket ticket);
        Task<bool> DeleteAsync(int ticketId);
        Task<int> InvalidateExpiredTicketsAsync(string currentMonth);
    }
}
