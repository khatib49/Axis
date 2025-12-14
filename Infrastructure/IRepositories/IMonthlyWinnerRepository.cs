using Domain.Models;

namespace Infrastructure.IRepositories
{
    public interface IMonthlyWinnerRepository
    {
        Task<MonthlyWinner?> GetByIdAsync(int winnerId);
        Task<MonthlyWinner?> GetByDrawMonthAsync(string drawMonth);
        Task<List<MonthlyWinner>> GetAllAsync();
        Task<List<MonthlyWinner>> GetByCustomerPhoneAsync(string phoneNumber);
        Task<List<MonthlyWinner>> GetUnclaimedAsync();
        Task<MonthlyWinner> CreateAsync(MonthlyWinner winner);
        Task<MonthlyWinner> UpdateAsync(MonthlyWinner winner);
        Task<bool> DeleteAsync(int winnerId);
    }
}
