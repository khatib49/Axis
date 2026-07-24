using Domain.Models;
using Infrastructure.IRepositories;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories
{
    public class LoyaltyTicketRepository : ILoyaltyTicketRepository
    {
        private readonly ApplicationDbContext _context;

        public LoyaltyTicketRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<LoyaltyTicket?> GetByIdAsync(int ticketId)
        {
            return await _context.LoyaltyTickets
                .Include(t => t.Customer)
                .FirstOrDefaultAsync(t => t.TicketId == ticketId);
        }

        public async Task<LoyaltyTicket?> GetByTransactionIdAsync(int transactionId)
        {
            return await _context.LoyaltyTickets
                .Include(t => t.Customer)
                .FirstOrDefaultAsync(t => t.TransactionId == transactionId);
        }

        public async Task<List<LoyaltyTicket>> GetByCustomerPhoneAsync(string phoneNumber)
        {
            return await _context.LoyaltyTickets
                .Where(t => t.CustomerPhone == phoneNumber)
                .OrderByDescending(t => t.EarnedDate)
                .ToListAsync();
        }

        public async Task<List<LoyaltyTicket>> GetByDrawMonthAsync(string drawMonth)
        {
            return await _context.LoyaltyTickets
                .Include(t => t.Customer)
                .Where(t => t.DrawMonth == drawMonth)
                .ToListAsync();
        }

        public async Task<List<LoyaltyTicket>> GetValidTicketsByDrawMonthAsync(string drawMonth)
        {
            return await _context.LoyaltyTickets
                .Include(t => t.Customer)
                .Where(t => t.DrawMonth == drawMonth && t.IsValid)
                .ToListAsync();
        }

        public async Task<List<LoyaltyTicket>> GetByCustomerAndMonthAsync(string phoneNumber, string drawMonth)
        {
            return await _context.LoyaltyTickets
                .Where(t => t.CustomerPhone == phoneNumber && t.DrawMonth == drawMonth && t.IsValid)
                .OrderByDescending(t => t.EarnedDate)
                .ToListAsync();
        }

        public async Task<List<LoyaltyTicket>> GetByDateRangeAsync(DateTime startDate, DateTime endDate)
        {
            return await _context.LoyaltyTickets
                .Include(t => t.Customer)
                .Where(t => t.EarnedDate >= startDate && t.EarnedDate < endDate && t.IsValid)
                .ToListAsync();
        }

        public async Task<List<LoyaltyTicket>> GetExpiredTicketsAsync(string currentMonth)
        {
            return await _context.LoyaltyTickets
                .Where(t => t.DrawMonth != currentMonth && t.IsValid)
                .ToListAsync();
        }

        public async Task<LoyaltyTicket> CreateAsync(LoyaltyTicket ticket)
        {
            _context.LoyaltyTickets.Add(ticket);
            await _context.SaveChangesAsync();
            return ticket;
        }

        public async Task<LoyaltyTicket> UpdateAsync(LoyaltyTicket ticket)
        {
            _context.LoyaltyTickets.Update(ticket);
            await _context.SaveChangesAsync();
            return ticket;
        }

        public async Task<bool> DeleteAsync(int ticketId)
        {
            var ticket = await GetByIdAsync(ticketId);
            if (ticket == null) return false;

            _context.LoyaltyTickets.Remove(ticket);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<int> InvalidateExpiredTicketsAsync(string currentMonth)
        {
            var expiredTickets = await GetExpiredTicketsAsync(currentMonth);

            foreach (var ticket in expiredTickets)
            {
                ticket.IsValid = false;
            }

            await _context.SaveChangesAsync();
            return expiredTickets.Count;
        }
    }
}
