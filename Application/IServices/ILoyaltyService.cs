using Application.DTOs;

namespace Application.IServices
{
    public interface ILoyaltyService
    {
        Task<CalculateTicketsResponse> CalculateAndAssignTicketsAsync(CalculateTicketsRequest request);
        Task<CustomerBalanceResponse?> GetCustomerBalanceAsync(string phoneNumber);
        Task<List<LeaderboardEntryDTO>> GetWeeklyLeaderboardAsync();
        Task<List<LeaderboardEntryDTO>> GetMonthlyLeaderboardAsync();
        Task<DrawResponse> ConductWeeklyDrawAsync(DrawRequest request);
        Task<DrawResponse> ConductMonthlyDrawAsync(DrawRequest request);
        Task InvalidateExpiredTicketsAsync();
    }
}
