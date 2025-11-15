using Domain.Entities;
// IMPORTANT for Npgsql extensions like UseSnakeCaseNamingConvention()
using Domain.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence
{
    public class ApplicationDbContext : IdentityDbContext<AppUser, AppRole, int>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options) { }

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

        public DbSet<Set> Sets => Set<Set>();
        public DbSet<Discount> Discounts => Set<Discount>();
        protected override void OnModelCreating(ModelBuilder b)
        {
            base.OnModelCreating(b);

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

        }
    }
}
