using Domain.Entities;
// IMPORTANT for Npgsql extensions like UseSnakeCaseNamingConvention()
using Domain.Identity;
using Domain.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using System.Reflection.Emit;
using System.Security.Principal;

namespace Infrastructure.Persistence
{
    public class ApplicationDbContext : IdentityDbContext<AppUser, AppRole, int>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options) { }

        // ADD THESE LINES TO ApplicationDbContext.cs
        public DbSet<AccountType> AccountTypes => Set<AccountType>();
        public DbSet<Account> Accounts => Set<Account>();
        public DbSet<JournalEntry> JournalEntries => Set<JournalEntry>();
        public DbSet<JournalEntryLine> JournalEntryLines => Set<JournalEntryLine>();
        public DbSet<Card> Cards => Set<Card>();
        public DbSet<UserCard> UserCards => Set<UserCard>();
        public DbSet<Game> Games => Set<Game>();
        public DbSet<Room> Rooms => Set<Room>();
        public DbSet<PassType> PassTypes => Set<PassType>();
        public DbSet<GameSession> GameSessions => Set<GameSession>();
        public DbSet<TransactionRecord> Transactions => Set<TransactionRecord>();
        public DbSet<Receipt> Receipts => Set<Receipt>();
        public DbSet<Setting> Settings => Set<Setting>();
        public DbSet<Category> Categories => Set<Category>();
        public DbSet<Item> Items => Set<Item>();
        public DbSet<CoffeeShopOrder> CoffeeShopOrders => Set<CoffeeShopOrder>();
        public DbSet<Expense> Expenses => Set<Expense>();
        public DbSet<Notification> Notifications => Set<Notification>();
        public DbSet<Status> Status => Set<Status>();

        public DbSet<TransactionItem> TransactionItems => Set<TransactionItem>();
        public DbSet<KitchenBarOrder> KitchenBarOrders { get; set; }

        public DbSet<Set> Sets => Set<Set>();
        public DbSet<Discount> Discounts => Set<Discount>();
        public DbSet<RoleCategory> RoleCategories { get; set; }
        public DbSet<LoyaltyTicket> LoyaltyTickets { get; set; }
        public DbSet<LoyaltyCustomer> LoyaltyCustomers { get; set; }
        public DbSet<WeeklyWinner> WeeklyWinners { get; set; }
        public DbSet<MonthlyWinner> MonthlyWinners { get; set; }
        public DbSet<TransactionAuditLog> TransactionAuditLogs => Set<TransactionAuditLog>();
        public DbSet<Channel> Channels => Set<Channel>();
        public DbSet<Ingredient> Ingredients => Set<Ingredient>();
        public DbSet<RecipeLine> RecipeLines => Set<RecipeLine>();
        public DbSet<StockMovement> StockMovements => Set<StockMovement>();
        public DbSet<Supplier> Suppliers => Set<Supplier>();
        public DbSet<Purchase> Purchases => Set<Purchase>();
        public DbSet<PurchaseLine> PurchaseLines => Set<PurchaseLine>();
        public DbSet<AdminAuditLog> AdminAuditLogs => Set<AdminAuditLog>();

        // AI chatbot + integrations
        public DbSet<IntegrationSetting> IntegrationSettings => Set<IntegrationSetting>();
        public DbSet<AiConversation>     AiConversations    => Set<AiConversation>();
        public DbSet<AiMessage>          AiMessages         => Set<AiMessage>();
        public DbSet<PendingAiAction>    PendingAiActions   => Set<PendingAiAction>();
        public DbSet<WhatsAppMessage>    WhatsAppMessages   => Set<WhatsAppMessage>();

        protected override void OnModelCreating(ModelBuilder b)
        {
            base.OnModelCreating(b);

            b.Entity<TransactionAuditLog>(e =>
            {
                e.ToTable("TransactionAuditLogs");
                e.HasKey(x => x.Id);
                e.Property(x => x.ChangedOn).HasDefaultValueSql("NOW()");
                e.HasOne(x => x.Transaction)
                .WithMany()
                .HasForeignKey(x => x.TransactionId)
                .OnDelete(DeleteBehavior.Cascade);
            });

            // Channel configuration. The Channels table and the
            // transactions.ChannelId column are added manually via a one-off
            // ALTER script (see deployment notes); this config keeps EF Core
            // aware of the mapping. SetNull on delete keeps historical
            // transactions intact even if a channel is later removed.
            b.Entity<Channel>(e =>
            {
                e.ToTable("Channels");
                e.HasKey(x => x.Id);
                e.Property(x => x.Name).IsRequired().HasMaxLength(100);
                e.Property(x => x.Description).HasMaxLength(500);
                e.Property(x => x.IsActive).HasDefaultValue(true);
                e.HasIndex(x => x.Name).IsUnique();
            });

            b.Entity<TransactionRecord>(e =>
            {
                e.HasOne(x => x.Channel)
                 .WithMany(c => c.Transactions)
                 .HasForeignKey(x => x.ChannelId)
                 .OnDelete(DeleteBehavior.SetNull);
                e.HasIndex(x => x.ChannelId);
            });

            // ─── Stock management ─────────────────────────────────────────
            // Tables, indexes, and FKs are also created manually via the
            // 2026-06-stock-management.sql script — this block keeps EF
            // Core's model in sync so future migrations don't try to
            // recreate them.
            b.Entity<Ingredient>(e =>
            {
                e.ToTable("Ingredients");
                e.HasKey(x => x.Id);
                e.Property(x => x.Name).IsRequired().HasMaxLength(150);
                e.Property(x => x.Unit).IsRequired().HasMaxLength(20);
                e.Property(x => x.QuantityOnHand).HasColumnType("numeric(18,3)");
                e.Property(x => x.ReorderLevel).HasColumnType("numeric(18,3)");
                e.Property(x => x.BuyPricePerUnit).HasColumnType("numeric(18,4)");
                e.Property(x => x.IsActive).HasDefaultValue(true);
                e.Property(x => x.CreatedOn).HasDefaultValueSql("NOW()");
                // Case-insensitive unique by Name (matches the LOWER(Name) DB index)
                e.HasIndex(x => x.Name).HasDatabaseName("IX_Ingredients_Name_LOWER");
            });

            b.Entity<RecipeLine>(e =>
            {
                e.ToTable("RecipeLines");
                e.HasKey(x => x.Id);
                e.Property(x => x.Quantity).HasColumnType("numeric(18,3)");
                e.Property(x => x.CreatedOn).HasDefaultValueSql("NOW()");

                e.HasOne(x => x.Item)
                 .WithMany(i => i.RecipeLines)
                 .HasForeignKey(x => x.ItemId)
                 .OnDelete(DeleteBehavior.Cascade);

                e.HasOne(x => x.Ingredient)
                 .WithMany(i => i.RecipeLines)
                 .HasForeignKey(x => x.IngredientId)
                 .OnDelete(DeleteBehavior.Restrict);

                // No two lines for the same (Item, Ingredient) pair
                e.HasIndex(x => new { x.ItemId, x.IngredientId }).IsUnique();
            });

            b.Entity<StockMovement>(e =>
            {
                e.ToTable("StockMovements");
                e.HasKey(x => x.Id);
                e.Property(x => x.Quantity).HasColumnType("numeric(18,3)");
                e.Property(x => x.BalanceAfter).HasColumnType("numeric(18,3)");
                e.Property(x => x.Type).IsRequired().HasMaxLength(50);
                e.Property(x => x.ReferenceType).HasMaxLength(50);
                e.Property(x => x.WasteReason).HasMaxLength(100);
                e.Property(x => x.CreatedBy).HasMaxLength(200);
                e.Property(x => x.CreatedOn).HasDefaultValueSql("NOW()");
                e.Property(x => x.UnitCost).HasColumnType("numeric(18,4)");
                e.Property(x => x.TotalCost).HasColumnType("numeric(18,2)");

                e.HasOne(x => x.Ingredient)
                 .WithMany(i => i.Movements)
                 .HasForeignKey(x => x.IngredientId)
                 .OnDelete(DeleteBehavior.Restrict);

                e.HasIndex(x => x.IngredientId);
                e.HasIndex(x => x.CreatedOn);
                e.HasIndex(x => new { x.ReferenceType, x.ReferenceId });
                e.HasIndex(x => x.Type);
            });

            // ─── Suppliers / Purchases (v2) ───────────────────────────────
            b.Entity<Supplier>(e =>
            {
                e.ToTable("Suppliers");
                e.HasKey(x => x.Id);
                e.Property(x => x.Name).IsRequired().HasMaxLength(150);
                e.Property(x => x.ContactInfo).HasMaxLength(500);
                e.Property(x => x.Notes).HasMaxLength(1000);
                e.Property(x => x.IsActive).HasDefaultValue(true);
                e.Property(x => x.CreatedOn).HasDefaultValueSql("NOW()");
                e.HasIndex(x => x.Name).HasDatabaseName("IX_Suppliers_Name_LOWER");
            });

            b.Entity<Purchase>(e =>
            {
                e.ToTable("Purchases");
                e.HasKey(x => x.Id);
                e.Property(x => x.InvoiceNumber).HasMaxLength(100);
                e.Property(x => x.TotalCost).HasColumnType("numeric(18,2)");
                e.Property(x => x.Notes).HasMaxLength(1000);
                e.Property(x => x.CreatedBy).HasMaxLength(200);
                e.Property(x => x.CreatedOn).HasDefaultValueSql("NOW()");

                e.HasOne(x => x.Supplier)
                 .WithMany(s => s.Purchases)
                 .HasForeignKey(x => x.SupplierId)
                 .OnDelete(DeleteBehavior.SetNull);

                e.HasIndex(x => x.SupplierId);
                e.HasIndex(x => x.PurchaseDate);
            });

            b.Entity<PurchaseLine>(e =>
            {
                e.ToTable("PurchaseLines");
                e.HasKey(x => x.Id);
                e.Property(x => x.Quantity).HasColumnType("numeric(18,3)");
                e.Property(x => x.UnitCost).HasColumnType("numeric(18,4)");
                e.Property(x => x.LineTotal).HasColumnType("numeric(18,2)");
                e.Property(x => x.Notes).HasMaxLength(500);

                e.HasOne(x => x.Purchase)
                 .WithMany(p => p.Lines)
                 .HasForeignKey(x => x.PurchaseId)
                 .OnDelete(DeleteBehavior.Cascade);

                e.HasOne(x => x.Ingredient)
                 .WithMany()
                 .HasForeignKey(x => x.IngredientId)
                 .OnDelete(DeleteBehavior.Restrict);

                e.HasIndex(x => x.PurchaseId);
                e.HasIndex(x => x.IngredientId);
            });

            b.Entity<AdminAuditLog>(e =>
            {
                e.ToTable("AdminAuditLogs");
                e.HasKey(x => x.Id);
                e.Property(x => x.EntityType).IsRequired().HasMaxLength(60);
                e.Property(x => x.EntityName).HasMaxLength(300);
                e.Property(x => x.Action).IsRequired().HasMaxLength(20);
                e.Property(x => x.ChangedBy).HasMaxLength(200);
                e.Property(x => x.ChangedOn).HasDefaultValueSql("NOW()");
                e.HasIndex(x => x.ChangedOn);
                e.HasIndex(x => x.EntityType);
                e.HasIndex(x => x.Action);
                e.HasIndex(x => x.ChangedBy);
                e.HasIndex(x => new { x.EntityType, x.EntityId });
            });

            // --- AI chatbot + integrations ---------------------------------
            // Columns are TIMESTAMPTZ in the DB (see 2026-06-ai-tz-fix.sql).
            // EF defaults to "timestamp with time zone" for DateTime under
            // Npgsql so no explicit HasColumnType needed — kept lean.
            b.Entity<IntegrationSetting>(e =>
            {
                e.ToTable("IntegrationSettings");
                e.HasKey(x => x.Id);
                e.Property(x => x.Key).IsRequired().HasMaxLength(80);
                e.HasIndex(x => x.Key).IsUnique();
                e.Property(x => x.Description).HasMaxLength(500);
                e.Property(x => x.UpdatedBy).HasMaxLength(200);
                e.Property(x => x.UpdatedOn).HasDefaultValueSql("NOW()");
            });

            b.Entity<AiConversation>(e =>
            {
                e.ToTable("AiConversations");
                e.HasKey(x => x.Id);
                e.Property(x => x.Title).IsRequired().HasMaxLength(200);
                e.Property(x => x.CreatedBy).HasMaxLength(200);
                e.Property(x => x.CreatedOn).HasDefaultValueSql("NOW()");
                e.Property(x => x.LastMessageOn).HasDefaultValueSql("NOW()");
                e.HasIndex(x => x.LastMessageOn);
                e.HasMany(x => x.Messages)
                    .WithOne(m => m.Conversation!)
                    .HasForeignKey(m => m.ConversationId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            b.Entity<AiMessage>(e =>
            {
                e.ToTable("AiMessages");
                e.HasKey(x => x.Id);
                e.Property(x => x.Role).IsRequired().HasMaxLength(20);
                e.Property(x => x.ToolCallId).HasMaxLength(100);
                e.Property(x => x.ToolName).HasMaxLength(100);
                e.Property(x => x.CreatedOn).HasDefaultValueSql("NOW()");
                e.HasIndex(x => new { x.ConversationId, x.Id });
            });

            b.Entity<PendingAiAction>(e =>
            {
                e.ToTable("PendingAiActions");
                e.HasKey(x => x.Id);
                e.Property(x => x.Type).IsRequired().HasMaxLength(50);
                e.Property(x => x.Title).IsRequired().HasMaxLength(300);
                e.Property(x => x.Payload).IsRequired();
                e.Property(x => x.Status).IsRequired().HasMaxLength(20);
                e.Property(x => x.ProposedBy).HasMaxLength(200);
                e.Property(x => x.ProposedOn).HasDefaultValueSql("NOW()");
                e.Property(x => x.DecidedBy).HasMaxLength(200);
                e.HasOne(x => x.Conversation)
                    .WithMany()
                    .HasForeignKey(x => x.ConversationId)
                    .OnDelete(DeleteBehavior.SetNull);
                e.HasIndex(x => new { x.Status, x.ProposedOn });
                e.HasIndex(x => x.ConversationId);
            });

            b.Entity<WhatsAppMessage>(e =>
            {
                e.ToTable("WhatsAppMessages");
                e.HasKey(x => x.Id);
                e.Property(x => x.RecipientPhone).IsRequired().HasMaxLength(40);
                e.Property(x => x.RecipientName).HasMaxLength(200);
                e.Property(x => x.TemplateName).HasMaxLength(80);
                e.Property(x => x.Status).IsRequired().HasMaxLength(20);
                e.Property(x => x.ProviderMessageId).HasMaxLength(100);
                e.Property(x => x.QueuedOn).HasDefaultValueSql("NOW()");
                e.HasOne(x => x.PendingAction)
                    .WithMany()
                    .HasForeignKey(x => x.PendingActionId)
                    .OnDelete(DeleteBehavior.SetNull);
                e.HasIndex(x => x.PendingActionId);
                e.HasIndex(x => new { x.Status, x.QueuedOn });
            });

            // KitchenBarOrder Configuration
            b.Entity<KitchenBarOrder>(entity =>
            {
                entity.ToTable("KitchenBarOrders");
                entity.HasKey(e => e.Id);

                entity.Property(e => e.ItemPrice).HasColumnType("decimal(18,2)");
                entity.Property(e => e.OrderedAt).HasDefaultValueSql("NOW()");
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("NOW()");

                entity.HasOne(e => e.Transaction)
                    .WithMany()
                    .HasForeignKey(e => e.TransactionId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.Item)
                    .WithMany()
                    .HasForeignKey(e => e.ItemId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.PreparedByUser)
                    .WithMany()
                    .HasForeignKey(e => e.PreparedBy)
                    .OnDelete(DeleteBehavior.SetNull);

                entity.HasIndex(e => new { e.Station, e.Status });
                entity.HasIndex(e => e.TransactionId);
                entity.HasIndex(e => e.OrderedAt);
                entity.HasIndex(e => e.Status);
            });

            // Relationships (keep only what you need; these are safe)
            b.Entity<UserCard>()
              .HasOne(x => x.User).WithMany()
              .HasForeignKey(x => x.UserId)
              .OnDelete(DeleteBehavior.Restrict);

            b.Entity<UserCard>()
              .HasOne(x => x.Card).WithMany(x => x.UserCards)
              .HasForeignKey(x => x.CardId)
              .OnDelete(DeleteBehavior.Cascade);

            b.Entity<PassType>()
              .HasOne(x => x.Game).WithMany(x => x.PassTypes)
              .HasForeignKey(x => x.GameId)
              .OnDelete(DeleteBehavior.Cascade);

            b.Entity<GameSession>()
              .HasOne(x => x.User).WithMany()
              .HasForeignKey(x => x.UserId)
              .OnDelete(DeleteBehavior.Restrict);

            b.Entity<GameSession>()
              .HasOne(x => x.Card).WithMany(x => x.GameSessions)
              .HasForeignKey(x => x.CardId)
              .OnDelete(DeleteBehavior.Restrict);

            b.Entity<GameSession>()
              .HasOne(x => x.Game).WithMany(x => x.GameSessions)
              .HasForeignKey(x => x.GameId)
              .OnDelete(DeleteBehavior.Restrict);

            b.Entity<GameSession>()
              .HasOne(x => x.Room).WithMany()
              .HasForeignKey(x => x.RoomId)
              .OnDelete(DeleteBehavior.Restrict);

            b.Entity<GameSession>()
              .HasOne(x => x.PassType).WithMany(x => x.GameSessions)
              .HasForeignKey(x => x.PassTypeId)
              .OnDelete(DeleteBehavior.Restrict);

            // AccountType configuration
            b.Entity<AccountType>(entity =>
            {
                entity.ToTable("account_types");
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.TypeName).IsUnique();
                entity.Property(e => e.IsActive).HasDefaultValue(true);
            });

            // Account configuration
            b.Entity<Account>(entity =>
            {
                entity.ToTable("accounts");
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.AccountNumber).IsUnique();
                entity.HasIndex(e => e.AccountName);
                entity.HasIndex(e => e.AccountTypeId);
                entity.HasIndex(e => e.ParentAccountId);
                entity.HasIndex(e => e.IsActive);

                entity.Property(e => e.CurrentBalance)
                    .HasPrecision(18, 2)
                    .HasDefaultValue(0);
                entity.Property(e => e.IsActive).HasDefaultValue(true);
                entity.Property(e => e.IsSystemAccount).HasDefaultValue(false);
                entity.Property(e => e.AllowManualEntry).HasDefaultValue(true);

                // Self-referencing relationship
                entity.HasOne(e => e.ParentAccount)
                    .WithMany(p => p.ChildAccounts)
                    .HasForeignKey(e => e.ParentAccountId)
                    .OnDelete(DeleteBehavior.Restrict);

                // AccountType relationship
                entity.HasOne(e => e.AccountType)
                    .WithMany(at => at.Accounts)
                    .HasForeignKey(e => e.AccountTypeId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // JournalEntry configuration
            b.Entity<JournalEntry>(entity =>
            {
                entity.ToTable("journal_entries");
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.EntryNumber).IsUnique();
                entity.HasIndex(e => e.EntryDate);
                entity.HasIndex(e => new { e.ReferenceType, e.ReferenceId });
                entity.HasIndex(e => e.IsPosted);
                entity.HasIndex(e => e.IsVoided);

                entity.Property(e => e.TotalAmount).HasPrecision(18, 2);
                entity.Property(e => e.IsPosted).HasDefaultValue(false);
                entity.Property(e => e.IsVoided).HasDefaultValue(false);
            });

            // JournalEntryLine configuration
            b.Entity<JournalEntryLine>(entity =>
            {
                entity.ToTable("journal_entry_lines");
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.JournalEntryId);
                entity.HasIndex(e => e.AccountId);

                entity.Property(e => e.DebitAmount)
                    .HasPrecision(18, 2)
                    .HasDefaultValue(0);
                entity.Property(e => e.CreditAmount)
                    .HasPrecision(18, 2)
                    .HasDefaultValue(0);

                // Relationships
                entity.HasOne(e => e.JournalEntry)
                    .WithMany(je => je.Lines)
                    .HasForeignKey(e => e.JournalEntryId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.Account)
                    .WithMany(a => a.JournalEntryLines)
                    .HasForeignKey(e => e.AccountId)
                    .OnDelete(DeleteBehavior.Restrict);
            });


            b.Entity<TransactionRecord>(e =>
            {
                e.ToTable("transactions");
                e.HasKey(x => x.Id);
                e.Property(x => x.TotalPrice).HasColumnType("decimal(18,2)");
                e.Property(x => x.CreatedOn).HasDefaultValueSql("NOW()");

                e.HasOne(x => x.Room)
                    .WithMany(r => r.Transactions)   // ← updated
                    .HasForeignKey(x => x.RoomId)
                    .OnDelete(DeleteBehavior.Restrict);

                e.HasOne(x => x.GameType)            // Category – stays without collection
                    .WithMany()
                    .HasForeignKey(x => x.GameTypeId)
                    .OnDelete(DeleteBehavior.Restrict);

                e.HasOne(x => x.Game)
                    .WithMany(g => g.Transactions)   // ← updated
                    .HasForeignKey(x => x.GameId)
                    .OnDelete(DeleteBehavior.Restrict);

                e.HasOne(x => x.GameSetting)         // Setting – stays without collection
                    .WithMany()
                    .HasForeignKey(x => x.GameSettingId)
                    .OnDelete(DeleteBehavior.Restrict);

                e.HasOne(x => x.Status)
                    .WithMany()
                    .HasForeignKey(x => x.StatusId)
                    .OnDelete(DeleteBehavior.Restrict);

                e.HasOne(x => x.Set)
                    .WithMany(rs => rs.Transactions)
                    .HasForeignKey(x => x.SetId)
                    .OnDelete(DeleteBehavior.SetNull); // safe
            });


            b.Entity<Receipt>()
              .HasOne(x => x.Transaction).WithMany()
              .HasForeignKey(x => x.TransactionId)
              .OnDelete(DeleteBehavior.Cascade);

            b.Entity<Receipt>()
              .HasOne(x => x.User).WithMany()
              .HasForeignKey(x => x.UserId)
              .OnDelete(DeleteBehavior.Restrict);

            b.Entity<Receipt>()
              .HasOne(x => x.Card).WithMany(x => x.Receipts)
              .HasForeignKey(x => x.CardId)
              .OnDelete(DeleteBehavior.Restrict);

            b.Entity<Setting>()
              .HasOne(x => x.Game).WithMany(x => x.Settings)
              .HasForeignKey(x => x.GameId)
              .OnDelete(DeleteBehavior.Cascade);

            b.Entity<Item>()
              .HasOne(x => x.Category).WithMany(x => x.Items)
              .HasForeignKey(x => x.CategoryId)
              .OnDelete(DeleteBehavior.Cascade);

            b.Entity<Item>()
              .HasOne(x => x.Game).WithMany(x => x.Items)
              .HasForeignKey(x => x.GameId)
              .OnDelete(DeleteBehavior.SetNull);
            
            b.Entity<Item>()
              .HasOne(x => x.Status).WithMany()
              .HasForeignKey(x => x.StatusId)
              .OnDelete(DeleteBehavior.Restrict);

            b.Entity<Game>()
              .HasOne(x => x.Status)
              .WithMany()
              .HasForeignKey(x => x.StatusId)
              .OnDelete(DeleteBehavior.Restrict);


            b.Entity<TransactionItem>(e =>
            {
                
                e.HasKey(ti => new { ti.TransactionRecordId, ti.ItemId });

                
                e.HasOne(ti => ti.TransactionRecord)
                    .WithMany(tr => tr.TransactionItems)
                    .HasForeignKey(ti => ti.TransactionRecordId)
                    .OnDelete(DeleteBehavior.Restrict);

                e.HasOne(ti => ti.Item)
                    .WithMany(i => i.TransactionItems)
                    .HasForeignKey(ti => ti.ItemId)
                    .OnDelete(DeleteBehavior.Restrict);

                
                e.HasIndex(ti => ti.ItemId);
                e.HasIndex(ti => ti.TransactionRecordId);
            });

            // --- RoomSet ---
            b.Entity<Set>(e =>
            {
                e.ToTable("Sets");
                e.HasKey(x => x.Id);

                e.Property(x => x.Name)
                    .IsRequired()
                    .HasMaxLength(64);

                e.HasOne(x => x.Room)
                    .WithMany(r => r.Sets)
                    .HasForeignKey(x => x.RoomId)
                    .OnDelete(DeleteBehavior.Restrict);

                // Each room cannot have duplicate set names (A, B, ...).
                e.HasIndex(x => new { x.RoomId, x.Name }).IsUnique();
            });

            // Existing TransactionItem config stays as-is…
            b.Entity<TransactionItem>(e =>
            {
                e.HasKey(ti => new { ti.TransactionRecordId, ti.ItemId });

                e.HasOne(ti => ti.TransactionRecord)
                    .WithMany(tr => tr.TransactionItems)
                    .HasForeignKey(ti => ti.TransactionRecordId)
                    .OnDelete(DeleteBehavior.Restrict);

                e.HasOne(ti => ti.Item)
                    .WithMany(i => i.TransactionItems)
                    .HasForeignKey(ti => ti.ItemId)
                    .OnDelete(DeleteBehavior.Restrict);

                e.HasIndex(ti => ti.ItemId);
                e.HasIndex(ti => ti.TransactionRecordId);
            });

            b.Entity<Expense>(e =>
            {
                e.ToTable("Expenses");

                // Map the property FK_CategoryId -> the existing DB column CategoryId
                e.Property(x => x.FK_CategoryId)
                 .HasColumnName("CategoryId");

                // Configure the relationship to Category
                e.HasOne(x => x.Category)
                 .WithMany(c => c.Expenses)      // make sure ExpenseCategory has ICollection<Expense> Expenses
                 .HasForeignKey(x => x.FK_CategoryId)
                 .OnDelete(DeleteBehavior.Restrict); // or your intended behavior
            });
            b.Entity<RoleCategory>(entity =>
            {
                entity.HasKey(e => e.Id);

                entity.HasIndex(e => e.RoleName);

                entity.HasIndex(e => new { e.RoleName, e.CategoryId })
                    .IsUnique();

                entity.HasOne(e => e.Category)
                    .WithMany()
                    .HasForeignKey(e => e.CategoryId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.Property(e => e.IsActive)
                    .HasDefaultValue(true);

                entity.Property(e => e.CreatedOn)
                    .HasDefaultValueSql("NOW()");
            });

            b.Entity<LoyaltyCustomer>(entity =>
            {
                entity.HasKey(e => e.PhoneNumber);
                entity.HasIndex(e => e.PhoneNumber).IsUnique();
                entity.HasIndex(e => e.TotalTicketsCurrentMonth);
            });

            // LoyaltyTicket configuration
            b.Entity<LoyaltyTicket>(entity =>
            {
                entity.HasKey(e => e.TicketId);
                entity.HasIndex(e => e.CustomerPhone);
                entity.HasIndex(e => e.TransactionId);
                entity.HasIndex(e => e.DrawMonth);
                entity.HasIndex(e => new { e.CustomerPhone, e.DrawMonth });
                entity.HasIndex(e => new { e.DrawMonth, e.IsValid });

                entity.HasOne(e => e.Customer)
                    .WithMany(c => c.Tickets)
                    .HasForeignKey(e => e.CustomerPhone)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // WeeklyWinner configuration
            b.Entity<WeeklyWinner>(entity =>
            {
                entity.HasKey(e => e.WinnerId);
                entity.HasIndex(e => e.CustomerPhone);
                entity.HasIndex(e => e.DrawWeek);
                entity.HasIndex(e => e.DrawDate);

                entity.HasOne(e => e.Customer)
                    .WithMany(c => c.WeeklyWins)
                    .HasForeignKey(e => e.CustomerPhone)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // MonthlyWinner configuration
            b.Entity<MonthlyWinner>(entity =>
            {
                entity.HasKey(e => e.WinnerId);
                entity.HasIndex(e => e.CustomerPhone);
                entity.HasIndex(e => e.DrawMonth);
                entity.HasIndex(e => e.DrawDate);

                entity.HasOne(e => e.Customer)
                    .WithMany(c => c.MonthlyWins)
                    .HasForeignKey(e => e.CustomerPhone)
                    .OnDelete(DeleteBehavior.Cascade);
            });
        }
    }
}
