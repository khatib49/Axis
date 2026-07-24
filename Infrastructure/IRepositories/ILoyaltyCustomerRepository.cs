using Domain.Models;

namespace Infrastructure.IRepositories
{
    public interface ILoyaltyCustomerRepository
    {
        Task<LoyaltyCustomer?> GetByPhoneAsync(string phoneNumber);
        Task<LoyaltyCustomer> CreateAsync(LoyaltyCustomer customer);
        Task<LoyaltyCustomer> UpdateAsync(LoyaltyCustomer customer);
        Task<bool> DeleteAsync(string phoneNumber);
        Task<List<LoyaltyCustomer>> GetTopCustomersByTicketsAsync(int count);
        Task<List<LoyaltyCustomer>> GetCustomersWithTicketsInMonthAsync(string drawMonth);
        Task<int> ResetMonthlyTicketsAsync(string currentMonth);
    }
}
