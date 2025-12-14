using Domain.Models;
using Infrastructure.IRepositories;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories
{
    public class LoyaltyCustomerRepository : ILoyaltyCustomerRepository
    {
        private readonly ApplicationDbContext _context;

        public LoyaltyCustomerRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<LoyaltyCustomer?> GetByPhoneAsync(string phoneNumber)
        {
            return await _context.LoyaltyCustomers
                .Include(c => c.Tickets.Where(t => t.IsValid))
                .FirstOrDefaultAsync(c => c.PhoneNumber == phoneNumber);
        }

        public async Task<LoyaltyCustomer> CreateAsync(LoyaltyCustomer customer)
        {
            _context.LoyaltyCustomers.Add(customer);
            await _context.SaveChangesAsync();
            return customer;
        }

        public async Task<LoyaltyCustomer> UpdateAsync(LoyaltyCustomer customer)
        {
            _context.LoyaltyCustomers.Update(customer);
            await _context.SaveChangesAsync();
            return customer;
        }

        public async Task<bool> DeleteAsync(string phoneNumber)
        {
            var customer = await GetByPhoneAsync(phoneNumber);
            if (customer == null) return false;

            _context.LoyaltyCustomers.Remove(customer);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<List<LoyaltyCustomer>> GetTopCustomersByTicketsAsync(int count)
        {
            return await _context.LoyaltyCustomers
                .Where(c => c.TotalTicketsCurrentMonth > 0)
                .OrderByDescending(c => c.TotalTicketsCurrentMonth)
                .Take(count)
                .ToListAsync();
        }

        public async Task<List<LoyaltyCustomer>> GetCustomersWithTicketsInMonthAsync(string drawMonth)
        {
            return await _context.LoyaltyCustomers
                .Include(c => c.Tickets.Where(t => t.DrawMonth == drawMonth && t.IsValid))
                .Where(c => c.Tickets.Any(t => t.DrawMonth == drawMonth && t.IsValid))
                .ToListAsync();
        }

        public async Task<int> ResetMonthlyTicketsAsync(string currentMonth)
        {
            var customers = await _context.LoyaltyCustomers
                .Where(c => c.TotalTicketsCurrentMonth > 0)
                .ToListAsync();

            foreach (var customer in customers)
            {
                var currentMonthTickets = await _context.LoyaltyTickets
                    .Where(t => t.CustomerPhone == customer.PhoneNumber &&
                               t.DrawMonth == currentMonth &&
                               t.IsValid)
                    .SumAsync(t => t.TicketsEarned);

                customer.TotalTicketsCurrentMonth = currentMonthTickets;
                customer.LastUpdated = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();
            return customers.Count;
        }
    }
}
