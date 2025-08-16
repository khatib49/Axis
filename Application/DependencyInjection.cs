using Application.IServices;
using Application.Mapping;
using Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Application
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddApplication(this IServiceCollection services)
        {
            services.AddSingleton<DomainMapper>(); // Riok mapper

            services.AddScoped<IAuthService, AuthService>();
            services.AddScoped<IGameService, GameService>();

            return services;
        }
    }
}
