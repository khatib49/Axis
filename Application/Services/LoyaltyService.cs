using Application.DTOs;
using Application.IServices;
using Domain.Models;
using Infrastructure.IRepositories;
using Microsoft.Extensions.Logging;
using System.Globalization;

namespace Application.Services
{
    public class LoyaltyService : ILoyaltyService
    {
        private readonly ILoyaltyTicketRepository _ticketRepository;
        private readonly ILoyaltyCustomerRepository _customerRepository;
        private readonly IWeeklyWinnerRepository _weeklyWinnerRepository;
        private readonly IMonthlyWinnerRepository _monthlyWinnerRepository;
        private readonly ILogger<LoyaltyService> _logger;
        private const decimal DOLLARS_PER_TICKET = 10m;

        public LoyaltyService(
            ILoyaltyTicketRepository ticketRepository,
            ILoyaltyCustomerRepository customerRepository,
            IWeeklyWinnerRepository weeklyWinnerRepository,
            IMonthlyWinnerRepository monthlyWinnerRepository,
            ILogger<LoyaltyService> logger)
        {
            _ticketRepository = ticketRepository;
            _customerRepository = customerRepository;
            _weeklyWinnerRepository = weeklyWinnerRepository;
            _monthlyWinnerRepository = monthlyWinnerRepository;
            _logger = logger;
        }

        public async Task<CalculateTicketsResponse> CalculateAndAssignTicketsAsync(CalculateTicketsRequest request)
        {
            try
            {
                // Validate request
                if (request.TotalAmount <= 0)
                {
                    return new CalculateTicketsResponse
                    {
                        Success = false,
                        Message = "Transaction amount must be greater than zero"
                    };
                }

                if (string.IsNullOrWhiteSpace(request.CustomerPhone))
                {
                    return new CalculateTicketsResponse
                    {
                        Success = false,
                        Message = "Customer phone number is required"
                    };
                }

                // Check if tickets already assigned for this transaction
                var existingTicket = await _ticketRepository.GetByTransactionIdAsync(request.TransactionId);

                if (existingTicket != null)
                {
                    _logger.LogWarning($"Tickets already assigned for transaction {request.TransactionId}");
                    return new CalculateTicketsResponse
                    {
                        Success = false,
                        Message = "Tickets already assigned for this transaction",
                        CustomerPhone = request.CustomerPhone
                    };
                }

                // Calculate tickets: $10 = 1 ticket, rounded down
                int ticketsEarned = (int)Math.Floor(request.TotalAmount / DOLLARS_PER_TICKET);

                if (ticketsEarned == 0)
                {
                    return new CalculateTicketsResponse
                    {
                        Success = true,
                        TicketsEarned = 0,
                        TotalTicketsThisMonth = 0,
                        Message = $"Transaction amount ${request.TotalAmount:F2} is below $10 threshold. No tickets earned.",
                        CustomerPhone = request.CustomerPhone
                    };
                }

                var now = DateTime.UtcNow;
                var drawMonth = now.ToString("yyyy-MM");

                // Get or create customer
                var customer = await _customerRepository.GetByPhoneAsync(request.CustomerPhone);

                if (customer == null)
                {
                    customer = new LoyaltyCustomer
                    {
                        PhoneNumber = request.CustomerPhone,
                        Name = request.CustomerName,
                        TotalTicketsCurrentMonth = 0,
                        CreatedAt = now,
                        LastUpdated = now
                    };
                    customer = await _customerRepository.CreateAsync(customer);
                }
                else if (!string.IsNullOrWhiteSpace(request.CustomerName) && string.IsNullOrWhiteSpace(customer.Name))
                {
                    customer.Name = request.CustomerName;
                    customer = await _customerRepository.UpdateAsync(customer);
                }

                // Create loyalty ticket record
                var loyaltyTicket = new LoyaltyTicket
                {
                    CustomerPhone = request.CustomerPhone,
                    TransactionId = request.TransactionId,
                    TicketsEarned = ticketsEarned,
                    EarnedDate = now,
                    DrawMonth = drawMonth,
                    IsValid = true,
                    CreatedAt = now
                };

                await _ticketRepository.CreateAsync(loyaltyTicket);

                // Update customer's total tickets for current month
                customer.TotalTicketsCurrentMonth += ticketsEarned;
                customer.LastUpdated = now;
                await _customerRepository.UpdateAsync(customer);

                _logger.LogInformation($"Assigned {ticketsEarned} tickets to {request.CustomerPhone} for transaction {request.TransactionId}");

                return new CalculateTicketsResponse
                {
                    Success = true,
                    TicketsEarned = ticketsEarned,
                    TotalTicketsThisMonth = customer.TotalTicketsCurrentMonth,
                    Message = $"Successfully earned {ticketsEarned} ticket(s)!",
                    CustomerPhone = request.CustomerPhone
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error calculating tickets for transaction {request.TransactionId}");
                return new CalculateTicketsResponse
                {
                    Success = false,
                    Message = $"Error processing tickets: {ex.Message}",
                    CustomerPhone = request.CustomerPhone
                };
            }
        }

        public async Task<CustomerBalanceResponse?> GetCustomerBalanceAsync(string phoneNumber)
        {
            try
            {
                var currentMonth = DateTime.UtcNow.ToString("yyyy-MM");

                var customer = await _customerRepository.GetByPhoneAsync(phoneNumber);

                if (customer == null)
                {
                    return new CustomerBalanceResponse
                    {
                        CustomerPhone = phoneNumber,
                        TotalTicketsCurrentMonth = 0,
                        CurrentMonth = currentMonth,
                        RecentTickets = new List<TicketDetailDTO>()
                    };
                }

                var recentTickets = await _ticketRepository.GetByCustomerAndMonthAsync(phoneNumber, currentMonth);

                var ticketDetails = recentTickets
                    .OrderByDescending(t => t.EarnedDate)
                    .Take(10)
                    .Select(t => new TicketDetailDTO
                    {
                        TicketId = t.TicketId,
                        TransactionId = t.TransactionId,
                        TicketsEarned = t.TicketsEarned,
                        EarnedDate = t.EarnedDate
                    })
                    .ToList();

                return new CustomerBalanceResponse
                {
                    CustomerPhone = customer.PhoneNumber,
                    CustomerName = customer.Name,
                    TotalTicketsCurrentMonth = customer.TotalTicketsCurrentMonth,
                    CurrentMonth = currentMonth,
                    RecentTickets = ticketDetails
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting balance for customer {phoneNumber}");
                return null;
            }
        }

        public async Task<List<LeaderboardEntryDTO>> GetWeeklyLeaderboardAsync()
        {
            try
            {
                var now = DateTime.UtcNow;
                var (weekStart, weekEnd) = GetWeekRange(now);

                var tickets = await _ticketRepository.GetByDateRangeAsync(weekStart, weekEnd);

                var leaderboard = tickets
                    .GroupBy(t => new { t.CustomerPhone, t.Customer?.Name })
                    .Select(g => new LeaderboardEntryDTO
                    {
                        CustomerPhone = g.Key.CustomerPhone,
                        CustomerName = g.Key.Name,
                        TotalTickets = g.Sum(t => t.TicketsEarned)
                    })
                    .OrderByDescending(l => l.TotalTickets)
                    .Take(50)
                    .ToList();

                // Assign ranks
                for (int i = 0; i < leaderboard.Count; i++)
                {
                    leaderboard[i].Rank = i + 1;
                }

                return leaderboard;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting weekly leaderboard");
                return new List<LeaderboardEntryDTO>();
            }
        }

        public async Task<List<LeaderboardEntryDTO>> GetMonthlyLeaderboardAsync()
        {
            try
            {
                var leaderboard = (await _customerRepository.GetTopCustomersByTicketsAsync(50))
                    .Select(c => new LeaderboardEntryDTO
                    {
                        CustomerPhone = c.PhoneNumber,
                        CustomerName = c.Name,
                        TotalTickets = c.TotalTicketsCurrentMonth
                    })
                    .ToList();

                // Assign ranks
                for (int i = 0; i < leaderboard.Count; i++)
                {
                    leaderboard[i].Rank = i + 1;
                }

                return leaderboard;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting monthly leaderboard");
                return new List<LeaderboardEntryDTO>();
            }
        }

        public async Task<DrawResponse> ConductWeeklyDrawAsync(DrawRequest request)
        {
            try
            {
                var now = DateTime.UtcNow;
                var drawWeek = string.IsNullOrWhiteSpace(request.DrawWeek)
                    ? GetISOWeekString(now)
                    : request.DrawWeek;

                // Get week range
                var (weekStart, weekEnd) = GetWeekRange(now);

                // Check if draw already conducted for this week
                var existingDraw = await _weeklyWinnerRepository.GetByDrawWeekAsync(drawWeek);

                if (existingDraw != null)
                {
                    return new DrawResponse
                    {
                        Success = false,
                        Message = $"Weekly draw already conducted for week {drawWeek}",
                        TotalEligibleCustomers = 0,
                        TotalTicketsInDraw = 0
                    };
                }

                // Get all eligible tickets for the week
                var eligibleTickets = await _ticketRepository.GetByDateRangeAsync(weekStart, weekEnd);

                if (!eligibleTickets.Any())
                {
                    return new DrawResponse
                    {
                        Success = false,
                        Message = "No eligible tickets for this week's draw",
                        TotalEligibleCustomers = 0,
                        TotalTicketsInDraw = 0
                    };
                }

                // Group by customer and calculate total tickets
                var customerTickets = eligibleTickets
                    .GroupBy(t => t.CustomerPhone)
                    .Select(g => new
                    {
                        CustomerPhone = g.Key,
                        CustomerName = g.First().Customer?.Name,
                        TotalTickets = g.Sum(t => t.TicketsEarned)
                    })
                    .ToList();

                // Create weighted list for random selection
                var weightedList = new List<string>();
                foreach (var customer in customerTickets)
                {
                    for (int i = 0; i < customer.TotalTickets; i++)
                    {
                        weightedList.Add(customer.CustomerPhone);
                    }
                }

                // Random selection
                var random = new Random();
                var winnerPhone = weightedList[random.Next(weightedList.Count)];
                var winner = customerTickets.First(c => c.CustomerPhone == winnerPhone);

                // Create winner record
                var weeklyWinner = new WeeklyWinner
                {
                    CustomerPhone = winner.CustomerPhone,
                    PrizeName = request.PrizeName,
                    DrawDate = now,
                    DrawWeek = drawWeek,
                    TicketsHeld = winner.TotalTickets,
                    Claimed = false,
                    CreatedAt = now
                };

                await _weeklyWinnerRepository.CreateAsync(weeklyWinner);

                _logger.LogInformation($"Weekly draw completed. Winner: {winner.CustomerPhone}, Prize: {request.PrizeName}");

                return new DrawResponse
                {
                    Success = true,
                    Message = "Weekly draw completed successfully",
                    Winner = new WinnerDTO
                    {
                        CustomerPhone = winner.CustomerPhone,
                        CustomerName = winner.CustomerName,
                        PrizeName = request.PrizeName,
                        TicketsHeld = winner.TotalTickets,
                        DrawDate = now
                    },
                    TotalEligibleCustomers = customerTickets.Count,
                    TotalTicketsInDraw = weightedList.Count
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error conducting weekly draw");
                return new DrawResponse
                {
                    Success = false,
                    Message = $"Error conducting draw: {ex.Message}",
                    TotalEligibleCustomers = 0,
                    TotalTicketsInDraw = 0
                };
            }
        }

        public async Task<DrawResponse> ConductMonthlyDrawAsync(DrawRequest request)
        {
            try
            {
                var now = DateTime.UtcNow;
                var drawMonth = string.IsNullOrWhiteSpace(request.DrawMonth)
                    ? now.ToString("yyyy-MM")
                    : request.DrawMonth;

                // Check if draw already conducted for this month
                var existingDraw = await _monthlyWinnerRepository.GetByDrawMonthAsync(drawMonth);

                if (existingDraw != null)
                {
                    return new DrawResponse
                    {
                        Success = false,
                        Message = $"Monthly draw already conducted for {drawMonth}",
                        TotalEligibleCustomers = 0,
                        TotalTicketsInDraw = 0
                    };
                }

                // Get all customers with tickets for the month
                var eligibleCustomers = await _customerRepository.GetCustomersWithTicketsInMonthAsync(drawMonth);

                if (!eligibleCustomers.Any())
                {
                    return new DrawResponse
                    {
                        Success = false,
                        Message = "No eligible customers for this month's draw",
                        TotalEligibleCustomers = 0,
                        TotalTicketsInDraw = 0
                    };
                }

                // Create weighted list for random selection
                var weightedList = new List<string>();
                foreach (var customer in eligibleCustomers)
                {
                    var ticketCount = customer.Tickets.Sum(t => t.TicketsEarned);
                    for (int i = 0; i < ticketCount; i++)
                    {
                        weightedList.Add(customer.PhoneNumber);
                    }
                }

                // Random selection
                var random = new Random();
                var winnerPhone = weightedList[random.Next(weightedList.Count)];
                var winner = eligibleCustomers.First(c => c.PhoneNumber == winnerPhone);
                var winnerTicketCount = winner.Tickets.Sum(t => t.TicketsEarned);

                // Create winner record
                var monthlyWinner = new MonthlyWinner
                {
                    CustomerPhone = winner.PhoneNumber,
                    PrizeName = request.PrizeName,
                    DrawMonth = drawMonth,
                    DrawDate = now,
                    TicketsHeld = winnerTicketCount,
                    Claimed = false,
                    CreatedAt = now
                };

                await _monthlyWinnerRepository.CreateAsync(monthlyWinner);

                _logger.LogInformation($"Monthly draw completed. Winner: {winner.PhoneNumber}, Prize: {request.PrizeName}");

                return new DrawResponse
                {
                    Success = true,
                    Message = "Monthly draw completed successfully",
                    Winner = new WinnerDTO
                    {
                        CustomerPhone = winner.PhoneNumber,
                        CustomerName = winner.Name,
                        PrizeName = request.PrizeName,
                        TicketsHeld = winnerTicketCount,
                        DrawDate = now
                    },
                    TotalEligibleCustomers = eligibleCustomers.Count,
                    TotalTicketsInDraw = weightedList.Count
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error conducting monthly draw");
                return new DrawResponse
                {
                    Success = false,
                    Message = $"Error conducting draw: {ex.Message}",
                    TotalEligibleCustomers = 0,
                    TotalTicketsInDraw = 0
                };
            }
        }

        public async Task InvalidateExpiredTicketsAsync()
        {
            try
            {
                var currentMonth = DateTime.UtcNow.ToString("yyyy-MM");

                // Invalidate tickets from previous months
                var invalidatedCount = await _ticketRepository.InvalidateExpiredTicketsAsync(currentMonth);

                // Reset customer monthly totals for new month
                var updatedCustomers = await _customerRepository.ResetMonthlyTicketsAsync(currentMonth);

                _logger.LogInformation($"Invalidated {invalidatedCount} expired tickets and updated {updatedCustomers} customer balances");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error invalidating expired tickets");
            }
        }

        private string GetISOWeekString(DateTime date)
        {
            var calendar = CultureInfo.CurrentCulture.Calendar;
            var week = calendar.GetWeekOfYear(date, CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);
            return $"{date.Year}-W{week:D2}";
        }

        private (DateTime weekStart, DateTime weekEnd) GetWeekRange(DateTime date)
        {
            var weekStart = date.AddDays(-(int)date.DayOfWeek + (int)DayOfWeek.Monday);
            if (date.DayOfWeek == DayOfWeek.Sunday)
            {
                weekStart = weekStart.AddDays(-7);
            }
            weekStart = weekStart.Date;
            var weekEnd = weekStart.AddDays(7);
            return (weekStart, weekEnd);
        }
    }
}
