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

            services.AddDbContext<ApplicationDbContext>(opt =>
            {
                opt.UseNpgsql(conn, b => b.MigrationsAssembly("Infrastructure"));
                // opt.UseSnakeCaseNamingConvention();
            });

            services.AddScoped(typeof(IBaseRepository<>), typeof(BaseRepository<>));
            services.AddScoped<IUnitOfWork, UnitOfWork>();

            return services;
        }
    }
}
