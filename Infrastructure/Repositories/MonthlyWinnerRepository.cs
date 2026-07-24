using Domain.Models;
using Infrastructure.IRepositories;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Repositories
{
    public class MonthlyWinnerRepository : IMonthlyWinnerRepository
    {
        private readonly ApplicationDbContext _context;

        public MonthlyWinnerRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<MonthlyWinner?> GetByIdAsync(int winnerId)
        {
            return await _context.MonthlyWinners
                .Include(w => w.Customer)
                .FirstOrDefaultAsync(w => w.WinnerId == winnerId);
        }

        public async Task<MonthlyWinner?> GetByDrawMonthAsync(string drawMonth)
        {
            return await _context.MonthlyWinners
                .Include(w => w.Customer)
                .FirstOrDefaultAsync(w => w.DrawMonth == drawMonth);
        }

        public async Task<List<MonthlyWinner>> GetAllAsync()
        {
            return await _context.MonthlyWinners
                .Include(w => w.Customer)
                .OrderByDescending(w => w.DrawDate)
                .ToListAsync();
        }

        public async Task<List<MonthlyWinner>> GetByCustomerPhoneAsync(string phoneNumber)
        {
            return await _context.MonthlyWinners
                .Where(w => w.CustomerPhone == phoneNumber)
                .OrderByDescending(w => w.DrawDate)
                .ToListAsync();
        }

        public async Task<List<MonthlyWinner>> GetUnclaimedAsync()
        {
            return await _context.MonthlyWinners
                .Include(w => w.Customer)
                .Where(w => !w.Claimed)
                .OrderBy(w => w.DrawDate)
                .ToListAsync();
        }

        public async Task<MonthlyWinner> CreateAsync(MonthlyWinner winner)
        {
            _context.MonthlyWinners.Add(winner);
            await _context.SaveChangesAsync();
            return winner;
        }

        public async Task<MonthlyWinner> UpdateAsync(MonthlyWinner winner)
        {
            _context.MonthlyWinners.Update(winner);
            await _context.SaveChangesAsync();
            return winner;
        }

        public async Task<bool> DeleteAsync(int winnerId)
        {
            var winner = await GetByIdAsync(winnerId);
            if (winner == null) return false;

            _context.MonthlyWinners.Remove(winner);
            await _context.SaveChangesAsync();
            return true;
        }
    }
}
