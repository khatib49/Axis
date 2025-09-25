using Domain.Entities;
// IMPORTANT for Npgsql extensions like UseSnakeCaseNamingConvention()
using Domain.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence
{
    public class ApplicationDbContext : IdentityDbContext<AppUser, AppRole, Guid>
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

            b.Entity<TransactionRecord>()
              .HasOne(x => x.User).WithMany()
              .HasForeignKey(x => x.UserId)
              .OnDelete(DeleteBehavior.Restrict);

            b.Entity<TransactionRecord>()
              .HasOne(x => x.Card).WithMany(x => x.Transactions)
              .HasForeignKey(x => x.CardId)
              .OnDelete(DeleteBehavior.Restrict);
            
            b.Entity<TransactionRecord>()
              .HasOne(x => x.Status)
              .WithMany() // Status does not have a collection of Games
              .HasForeignKey(x => x.StatusId)
              .OnDelete(DeleteBehavior.Restrict);

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
        }
    }
}
