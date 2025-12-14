namespace Application.DTOs
{
    public class CalculateTicketsRequest
    {
        public int TransactionId { get; set; }
        public decimal TotalAmount { get; set; }
        public string CustomerPhone { get; set; } = string.Empty;
        public string? CustomerName { get; set; }
    }
    public class CalculateTicketsResponse
    {
        public bool Success { get; set; }
        public int TicketsEarned { get; set; }
        public int TotalTicketsThisMonth { get; set; }
        public string Message { get; set; } = string.Empty;
        public string CustomerPhone { get; set; } = string.Empty;
    }

    public class CustomerBalanceResponse
    {
        public string CustomerPhone { get; set; } = string.Empty;
        public string? CustomerName { get; set; }
        public int TotalTicketsCurrentMonth { get; set; }
        public string CurrentMonth { get; set; } = string.Empty;
        public List<TicketDetailDTO> RecentTickets { get; set; } = new();
    }

    public class TicketDetailDTO
    {
        public int TicketId { get; set; }
        public int TransactionId { get; set; }
        public int TicketsEarned { get; set; }
        public DateTime EarnedDate { get; set; }
    }

    public class LeaderboardEntryDTO
    {
        public string CustomerPhone { get; set; } = string.Empty;
        public string? CustomerName { get; set; }
        public int TotalTickets { get; set; }
        public int Rank { get; set; }
    }

    public class DrawRequest
    {
        public string PrizeName { get; set; } = string.Empty;
        public string? DrawMonth { get; set; } // Optional, defaults to current month
        public string? DrawWeek { get; set; } // Optional for weekly, defaults to current week
    }

    public class DrawResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public WinnerDTO? Winner { get; set; }
        public int TotalEligibleCustomers { get; set; }
        public int TotalTicketsInDraw { get; set; }
    }

    public class WinnerDTO
    {
        public string CustomerPhone { get; set; } = string.Empty;
        public string? CustomerName { get; set; }
        public string PrizeName { get; set; } = string.Empty;
        public int TicketsHeld { get; set; }
        public DateTime DrawDate { get; set; }
    }
}
