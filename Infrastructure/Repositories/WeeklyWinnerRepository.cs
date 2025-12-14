using Domain.Models;
using Infrastructure.IRepositories;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories
{
    public class WeeklyWinnerRepository : IWeeklyWinnerRepository
    {
        private readonly ApplicationDbContext _context;

        public WeeklyWinnerRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<WeeklyWinner?> GetByIdAsync(int winnerId)
        {
            return await _context.WeeklyWinners
                .Include(w => w.Customer)
                .FirstOrDefaultAsync(w => w.WinnerId == winnerId);
        }

        public async Task<WeeklyWinner?> GetByDrawWeekAsync(string drawWeek)
        {
            return await _context.WeeklyWinners
                .Include(w => w.Customer)
                .FirstOrDefaultAsync(w => w.DrawWeek == drawWeek);
        }

        public async Task<List<WeeklyWinner>> GetAllAsync()
        {
            return await _context.WeeklyWinners
                .Include(w => w.Customer)
                .OrderByDescending(w => w.DrawDate)
                .ToListAsync();
        }

        public async Task<List<WeeklyWinner>> GetByCustomerPhoneAsync(string phoneNumber)
        {
            return await _context.WeeklyWinners
                .Where(w => w.CustomerPhone == phoneNumber)
                .OrderByDescending(w => w.DrawDate)
                .ToListAsync();
        }

        public async Task<List<WeeklyWinner>> GetUnclaimedAsync()
        {
            return await _context.WeeklyWinners
                .Include(w => w.Customer)
                .Where(w => !w.Claimed)
                .OrderBy(w => w.DrawDate)
                .ToListAsync();
        }

        public async Task<WeeklyWinner> CreateAsync(WeeklyWinner winner)
        {
            _context.WeeklyWinners.Add(winner);
            await _context.SaveChangesAsync();
            return winner;
        }

        public async Task<WeeklyWinner> UpdateAsync(WeeklyWinner winner)
        {
            _context.WeeklyWinners.Update(winner);
            await _context.SaveChangesAsync();
            return winner;
        }

        public async Task<bool> DeleteAsync(int winnerId)
        {
            var winner = await GetByIdAsync(winnerId);
            if (winner == null) return false;

            _context.WeeklyWinners.Remove(winner);
            await _context.SaveChangesAsync();
            return true;
        }
    }

}
