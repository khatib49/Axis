using Domain.Models;

namespace Infrastructure.IRepositories
{
    public interface IWeeklyWinnerRepository
    {
        Task<WeeklyWinner?> GetByIdAsync(int winnerId);
        Task<WeeklyWinner?> GetByDrawWeekAsync(string drawWeek);
        Task<List<WeeklyWinner>> GetAllAsync();
        Task<List<WeeklyWinner>> GetByCustomerPhoneAsync(string phoneNumber);
        Task<List<WeeklyWinner>> GetUnclaimedAsync();
        Task<WeeklyWinner> CreateAsync(WeeklyWinner winner);
        Task<WeeklyWinner> UpdateAsync(WeeklyWinner winner);
        Task<bool> DeleteAsync(int winnerId);
    }
}
