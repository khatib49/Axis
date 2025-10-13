namespace Application.DTOs
{
    public record CategoryDto(int Id, string Name , string Type);
    public record CategoryCreateDto(string Name , string Type);
    public record CategoryUpdateDto(string? Name , string Type);

    public record ItemDto(int Id, string Name, int Quantity, decimal Price, string Type, int CategoryId, int? StatusId, string? ImagePath);
    //public record ItemCreateDto(string Name, int Quantity, decimal Price, string Type, int CategoryId, int? StatusId);
    //public record ItemUpdateDto(string? Name, int? Quantity, decimal? Price, string? Type, int? CategoryId, int? StatusId);

    public record TransactionsFilterDto : BasePaginationRequestDto
    {
        public List<int>? StatusIds { get; set; }     // filters TransactionRecord.StatusId
        public List<int>? CategoryIds { get; set; }   // Items: Item.CategoryId; Games: Game.CategoryId
        public List<string>? CreatedBy { get; set; }  // filters TransactionRecord.CreatedBy (exact match)
        public DateTime? From { get; set; }           // CreatedOn >= From (inclusive)
        public DateTime? To { get; set; }             // CreatedOn < To (exclusive)
        public string? Search { get; set; }           // fuzzy search field (see implementations)
    }

    // One row per TransactionItem inside a TransactionRecord
    public class ItemTransactionLineDto
    {
        // Transaction
        public int TransactionId { get; set; }
        public DateTime CreatedOn { get; set; }
        public int StatusId { get; set; }
        public string CreatedBy { get; set; } = string.Empty;

        // Item line
        public int ItemId { get; set; }
        public string ItemName { get; set; } = string.Empty;
        public int CategoryId { get; set; }
        public string? CategoryName { get; set; }
        public string ItemType { get; set; } = string.Empty;
        public int OrderedQuantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal LineTotal { get; set; }
        public string? ImagePath { get; set; }
    }

    // One row per TransactionRecord (game transaction)
    public class GameTransactionDetailsDto
    {
        public int TransactionId { get; set; }
        public DateTime CreatedOn { get; set; }
        public int StatusId { get; set; }
        public string CreatedBy { get; set; } = string.Empty;

        public int? RoomId { get; set; }
        public string? RoomName { get; set; }

        public int? GameTypeId { get; set; }
        public string? GameTypeName { get; set; }

        public int? GameId { get; set; }
        public string GameName { get; set; } = string.Empty;
        public int? GameCategoryId { get; set; }
        public string? GameCategoryName { get; set; }

        public int? GameSettingId { get; set; }
        public string? GameSettingName { get; set; }

        public int Hours { get; set; }
        public decimal TotalPrice { get; set; }
    }

}
