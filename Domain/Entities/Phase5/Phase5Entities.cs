using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    // ── Tournaments ─────────────────────────────────────────────────────
    [Table("Tour_Tournaments")]
    public class Tournament
    {
        [Key] public int Id { get; set; }
        [Required][MaxLength(150)] public string Name { get; set; } = default!;
        /// <summary>Free text shown to players, e.g. "EA FC 26", "Pokémon TCG".</summary>
        [Required][MaxLength(120)] public string GameTitle { get; set; } = default!;
        [MaxLength(1000)] public string? Rules { get; set; }
        public DateTime StartsOn { get; set; }
        public int MaxPlayers { get; set; } = 16;
        [Column(TypeName = "numeric(18,2)")] public decimal EntryFee { get; set; }
        /// <summary>Prize pool paid out at the end (USD).</summary>
        [Column(TypeName = "numeric(18,2)")] public decimal PrizePool { get; set; }
        /// <summary>Percent of the pool per place, e.g. "70,30".</summary>
        [Required][MaxLength(60)] public string PrizeSplit { get; set; } = "70,30";
        /// <summary>Rooms whose stations host matches (comma ids). Empty = any room.</summary>
        [MaxLength(200)] public string? RoomIds { get; set; }
        /// <summary>Draft | Open | Running | Completed | Cancelled</summary>
        [Required][MaxLength(20)] public string Status { get; set; } = TournamentStatuses.Draft;
        /// <summary>Public link token (bracket + sign-up page).</summary>
        [Required][MaxLength(40)] public string PublicToken { get; set; } = default!;
        public bool PrizesPaid { get; set; }
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        [Required][MaxLength(256)] public string CreatedBy { get; set; } = "system";
        public int? BranchId { get; set; }
    }

    [Table("Tour_Participants")]
    public class TournamentParticipant
    {
        [Key] public int Id { get; set; }
        public int TournamentId { get; set; }
        [Required][MaxLength(80)] public string DisplayName { get; set; } = default!;
        [MaxLength(40)] public string? Phone { get; set; }
        /// <summary>Client account (for wallet entry fee / prize).</summary>
        public int? UserId { get; set; }
        public bool Paid { get; set; }
        /// <summary>Counter | Wallet | Free</summary>
        [MaxLength(20)] public string? PaidVia { get; set; }
        public int? Seed { get; set; }
        public int? FinalPlace { get; set; }
        [Column(TypeName = "numeric(18,2)")] public decimal PrizeAmount { get; set; }
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
    }

    [Table("Tour_Matches")]
    public class TournamentMatch
    {
        [Key] public int Id { get; set; }
        public int TournamentId { get; set; }
        /// <summary>1 = first round.</summary>
        public int Round { get; set; }
        /// <summary>Position within the round, 0-based.</summary>
        public int Slot { get; set; }
        public int? Player1Id { get; set; }
        public int? Player2Id { get; set; }
        public int? WinnerId { get; set; }
        [MaxLength(40)] public string? Score { get; set; }
        /// <summary>Station (Set) assigned to the match.</summary>
        public int? SetId { get; set; }
        /// <summary>Pending | Ready | Playing | Done</summary>
        [Required][MaxLength(10)] public string Status { get; set; } = "Pending";
        public DateTime? CompletedOn { get; set; }
    }

    public static class TournamentStatuses
    {
        public const string Draft = "Draft";
        public const string Open = "Open";
        public const string Running = "Running";
        public const string Completed = "Completed";
        public const string Cancelled = "Cancelled";
    }

    // ── Room / station booking ──────────────────────────────────────────
    [Table("Booking_StationBookings")]
    public class StationBooking
    {
        [Key] public int Id { get; set; }
        /// <summary>Station (Set) booked.</summary>
        public int SetId { get; set; }
        public DateTime StartsOn { get; set; }
        public DateTime EndsOn { get; set; }
        [Required][MaxLength(120)] public string CustomerName { get; set; } = default!;
        [Required][MaxLength(40)] public string Phone { get; set; } = default!;
        public int Players { get; set; } = 1;
        [MaxLength(500)] public string? Notes { get; set; }
        /// <summary>Pending | Confirmed | CheckedIn | Completed | NoShow | Cancelled</summary>
        [Required][MaxLength(20)] public string Status { get; set; } = BookingStatuses.Confirmed;
        /// <summary>Guest link / QR check-in token.</summary>
        [Required][MaxLength(40)] public string Token { get; set; } = default!;
        /// <summary>Web | Staff</summary>
        [Required][MaxLength(10)] public string Source { get; set; } = "Web";
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        public DateTime? CheckedInOn { get; set; }
        [MaxLength(256)] public string? HandledBy { get; set; }
        public int? BranchId { get; set; }
    }

    public static class BookingStatuses
    {
        public const string Pending = "Pending";
        public const string Confirmed = "Confirmed";
        public const string CheckedIn = "CheckedIn";
        public const string Completed = "Completed";
        public const string NoShow = "NoShow";
        public const string Cancelled = "Cancelled";
        public static readonly string[] Blocking = { Pending, Confirmed, CheckedIn };
    }

    // ── Order from the station ──────────────────────────────────────────
    [Table("Station_Codes")]
    public class StationCode
    {
        [Key] public int Id { get; set; }
        public int SetId { get; set; }
        [Required][MaxLength(40)] public string Token { get; set; } = default!;
        public bool IsActive { get; set; } = true;
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
    }

    // ── Owner bot (Telegram / WhatsApp) ─────────────────────────────────
    [Table("OwnerBot_Chats")]
    public class OwnerBotChat
    {
        [Key] public int Id { get; set; }
        /// <summary>Telegram | WhatsApp</summary>
        [Required][MaxLength(10)] public string Channel { get; set; } = default!;
        [Required][MaxLength(60)] public string ChatId { get; set; } = default!;
        [MaxLength(120)] public string? DisplayName { get; set; }
        /// <summary>AI conversation that keeps the context of this chat.</summary>
        public int? ConversationId { get; set; }
        /// <summary>Allowed by the admin (unknown chats only get their id back).</summary>
        public bool IsAllowed { get; set; }
        public DateTime FirstSeenOn { get; set; } = DateTime.UtcNow;
        public DateTime? LastMessageOn { get; set; }
    }

    // ── Parent wallet ───────────────────────────────────────────────────
    [Table("Parent_Links")]
    public class ParentLink
    {
        [Key] public int Id { get; set; }
        /// <summary>The child's client account (owner of the wallet).</summary>
        public int ChildUserId { get; set; }
        [Required][MaxLength(120)] public string ParentName { get; set; } = default!;
        [MaxLength(40)] public string? ParentPhone { get; set; }
        [MaxLength(60)] public string? ParentTelegramChatId { get; set; }
        [MaxLength(200)] public string? ParentEmail { get; set; }
        /// <summary>Max wallet spend per venue day. Null = no limit.</summary>
        [Column(TypeName = "numeric(18,2)")] public decimal? DailyLimit { get; set; }
        [Column(TypeName = "numeric(18,2)")] public decimal? LowBalanceThreshold { get; set; }
        public bool NotifyOnSpend { get; set; } = true;
        public bool NotifyOnSessionEnd { get; set; } = true;
        public bool IsActive { get; set; } = true;
        /// <summary>Read-only parent page link.</summary>
        [Required][MaxLength(40)] public string Token { get; set; } = default!;
        public DateTime? LastLowBalanceAlertOn { get; set; }
        public int LastNotifiedTransactionId { get; set; }
        /// <summary>Last wallet movement the parent was told about.</summary>
        public int LastNotifiedWalletTxId { get; set; }
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
    }

    // ── Owner alerts ────────────────────────────────────────────────────
    [Table("Alert_Rules")]
    public class AlertRule
    {
        [Key] public int Id { get; set; }
        [Required][MaxLength(120)] public string Name { get; set; } = default!;
        /// <summary>See AlertMetrics.</summary>
        [Required][MaxLength(40)] public string Metric { get; set; } = default!;
        /// <summary>Above | Below</summary>
        [Required][MaxLength(10)] public string Comparator { get; set; } = "Above";
        [Column(TypeName = "numeric(18,2)")] public decimal Threshold { get; set; }
        /// <summary>Minutes used by time-based metrics (e.g. pending for more than N minutes).</summary>
        public int WindowMinutes { get; set; } = 30;
        /// <summary>Only check between these venue hours (0-23). Same value = all day.</summary>
        public int FromHour { get; set; }
        public int ToHour { get; set; }
        /// <summary>Comma list: Telegram, WhatsApp, Email, InApp</summary>
        [Required][MaxLength(80)] public string Channels { get; set; } = "Telegram,InApp";
        /// <summary>Extra recipients (comma): Telegram chat ids, phone numbers, emails.</summary>
        [MaxLength(500)] public string? Recipients { get; set; }
        public int CooldownMinutes { get; set; } = 60;
        public bool IsActive { get; set; } = true;
        public DateTime? LastFiredOn { get; set; }
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
    }

    [Table("Alert_Events")]
    public class AlertEvent
    {
        [Key] public int Id { get; set; }
        public int RuleId { get; set; }
        [Column(TypeName = "numeric(18,2)")] public decimal Value { get; set; }
        [Required][MaxLength(500)] public string Message { get; set; } = default!;
        [MaxLength(300)] public string? Delivery { get; set; }
        public DateTime FiredOn { get; set; } = DateTime.UtcNow;
    }

    public static class AlertMetrics
    {
        public const string SalesToday = "SalesToday";
        public const string NoSaleMinutes = "NoSaleMinutes";
        public const string LargeSale = "LargeSale";
        public const string VoidRequestsPending = "VoidRequestsPending";
        public const string VoidAmountToday = "VoidAmountToday";
        public const string LowStockCount = "LowStockCount";
        public const string OpenInvoicesOlderThan = "OpenInvoicesOlderThan";
        public const string DeliveryPendingOlderThan = "DeliveryPendingOlderThan";
        public const string CashVariance = "CashVariance";
        public const string StationsBusyPercent = "StationsBusyPercent";
        public static readonly string[] All = { SalesToday, NoSaleMinutes, LargeSale, VoidRequestsPending, VoidAmountToday, LowStockCount, OpenInvoicesOlderThan, DeliveryPendingOlderThan, CashVariance, StationsBusyPercent };
    }

    // ── AI drafts (menu import / supplier invoice scan) ─────────────────
    [Table("Ai_ImportDrafts")]
    public class AiImportDraft
    {
        [Key] public int Id { get; set; }
        /// <summary>Menu | Invoice</summary>
        [Required][MaxLength(10)] public string Kind { get; set; } = default!;
        [Required][MaxLength(200)] public string FileName { get; set; } = default!;
        /// <summary>Extracted JSON (edited by the admin before applying).</summary>
        public string Json { get; set; } = "{}";
        /// <summary>Draft | Applied | Discarded</summary>
        [Required][MaxLength(10)] public string Status { get; set; } = "Draft";
        [MaxLength(500)] public string? Result { get; set; }
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        [Required][MaxLength(256)] public string CreatedBy { get; set; } = "system";
        public DateTime? AppliedOn { get; set; }
    }
}
