using Infrastructure.IRepositories;
using Infrastructure.Persistence;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration cfg)
        {
            var conn = cfg.GetConnectionString("Postgres");

            // Interceptor needs HttpContext to read the current user's
            // identity. Register it as scoped (one per request).
            services.AddScoped<AdminAuditInterceptor>();

            services.AddDbContext<ApplicationDbContext>((sp, opt) =>
            {
                opt.UseNpgsql(conn, b => b.MigrationsAssembly("Infrastructure"));
                // opt.UseSnakeCaseNamingConvention();

                // Wire the SaveChanges interceptor that writes
                // AdminAuditLog rows for every Create/Update/Delete on the
                // allow-listed entities. Runs inside the same DB transaction
                // as the actual change, so audits are atomic with the work.
                opt.AddInterceptors(sp.GetRequiredService<AdminAuditInterceptor>());
            });


            // Register Loyalty Repositories
            services.AddScoped<ILoyaltyTicketRepository, LoyaltyTicketRepository>();
            services.AddScoped<ILoyaltyCustomerRepository, LoyaltyCustomerRepository>();
            services.AddScoped<IWeeklyWinnerRepository, WeeklyWinnerRepository>();
            services.AddScoped<IMonthlyWinnerRepository, MonthlyWinnerRepository>();

            services.AddScoped(typeof(IBaseRepository<>), typeof(BaseRepository<>));
            services.AddScoped<IUnitOfWork, UnitOfWork>();

            return services;
        }
    }
}
