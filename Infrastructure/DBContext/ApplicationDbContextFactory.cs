using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Infrastructure.DBContext
{
    public class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext(string[] args)
        {
            var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();

            // You can read from env OR fall back to a hardcoded dev connection
            var cs = Environment.GetEnvironmentVariable("POSTGRES_CONNECTION")
                     ?? "Host=localhost;Port=5432;Database=axis_db;Username=postgres;Password=admin;";

            optionsBuilder.UseNpgsql(cs, b => b.MigrationsAssembly("Infrastructure"));

            return new ApplicationDbContext(optionsBuilder.Options);
        }
    }
}
