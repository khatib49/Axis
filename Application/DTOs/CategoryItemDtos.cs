namespace Application.DTOs
{
    public record CategoryDto(int Id, string Name , string Type , string? ItemType);
    public record CategoryCreateDto(string Name , string Type , string? ItemType);
    public record CategoryUpdateDto(string? Name , string Type , string? ItemType);

    /// <summary>A paid extra an item offers (customize sheet at the cashier).</summary>
    public record ItemAddOnDto(int Id, string Name, decimal Price, bool IsActive, int SortOrder);

    /// <summary>What the admin add-ons editor posts back (replace-all list).</summary>
    public record ItemAddOnUpsertDto(int? Id, string Name, decimal Price, bool IsActive = true, int SortOrder = 0);

    public record ItemDto(int Id, string Name, int Quantity, decimal Price, string Type, int CategoryId, int? StatusId, string? ImagePath, decimal? BuyPrice,
        // Trailing default keeps positional callers valid.
        List<ItemAddOnDto>? AddOns = null);
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
        public string? Comment { get; set; }
    }


    // Reusable item shape inside a transaction
    public class TransactionItemMiniDto
    {
        public int ItemId { get; set; }
        public string ItemName { get; set; } = string.Empty;

        public int CategoryId { get; set; }
        public string? CategoryName { get; set; }

        public string ItemType { get; set; } = string.Empty;

        public int Quantity { get; set; }          // ordered qty
        public decimal UnitPrice { get; set; }
        public decimal LineTotal { get; set; }

        /// <summary>Bundled with an event setting — handed over, not charged.</summary>
        public bool IsIncluded { get; set; }

        /// <summary>Paid extras chosen for this line (snapshotted at sale time).</summary>
        public List<OrderLineAddOnDto> AddOns { get; set; } = new();

        public string? ImagePath { get; set; }
    }
    /// <summary>
    /// One row per TransactionRecord, focused on items (replaces the flat ItemTransactionLineDto).
    /// </summary>
    public class ItemTransactionDto
    {
        // Transaction
        public int TransactionId { get; set; }
        public DateTime CreatedOn { get; set; }
        public int StatusId { get; set; }
        public string CreatedBy { get; set; } = string.Empty;

        // Optional room / set (use if you store set on transactions)
        public int? RoomId { get; set; }
        public string? RoomName { get; set; }
        public int? SetId { get; set; }
        public string? SetName { get; set; }

        // Totals
        public decimal Hours { get; set; }
        public decimal TotalPrice { get; set; }
        public string? Comment { get; set; }

        // Surfaced so the admin panel and the printed receipt can show the
        // whole record rather than a subset.
        public int NumberOfPersons { get; set; } = 1;
        public int? UserId { get; set; }
        public string? UserName { get; set; }
        public int? ChannelId { get; set; }
        public string? ChannelName { get; set; }
        public DateTime? ModifiedOn { get; set; }

        // Items inside this transaction
        public List<TransactionItemMiniDto> Items { get; set; } = new();
        public DiscountDto? Discount { get; set; }
    }

    /// <summary>
    /// One row per TransactionRecord (game transaction) with its items included.
    /// </summary>
    public class GameTransactionDetailsDto
    {
        public int TransactionId { get; set; }
        public DateTime CreatedOn { get; set; }
        public int StatusId { get; set; }
        public string CreatedBy { get; set; } = string.Empty;

        public int? RoomId { get; set; }
        public string? RoomName { get; set; }

        // Optional set on the room (if applicable)
        public int? SetId { get; set; }
        public string? SetName { get; set; }

        public int? GameTypeId { get; set; }
        public string? GameTypeName { get; set; }

        public int? GameId { get; set; }
        public string GameName { get; set; } = string.Empty;

        public int? GameCategoryId { get; set; }
        public string? GameCategoryName { get; set; }

        public int? GameSettingId { get; set; }
        public string? GameSettingName { get; set; }

        public decimal Hours { get; set; }
        public decimal TotalPrice { get; set; }
        public string? Comment { get; set; }
        public DiscountDto? Discount {get;set; }

        // Everything below was on the entity but never reached the admin
        // screen, so the panel could only show a partial picture of a
        // session. Surfaced now so admins see the whole record.
        public int NumberOfPersons { get; set; } = 1;

        /// <summary>Client attached to the session, if any.</summary>
        public int? UserId { get; set; }
        public string? UserName { get; set; }

        /// <summary>Sales channel (Toters, phone, walk-in…).</summary>
        public int? ChannelId { get; set; }
        public string? ChannelName { get; set; }

        /// <summary>Auto-close time for open sessions.</summary>
        public DateTime? ExpectedEndOn { get; set; }
        public DateTime? ModifiedOn { get; set; }
        public bool IsDayPass { get; set; }

        // Items inside this game transaction
        public List<TransactionItemMiniDto> Items { get; set; } = new();
    }

}
