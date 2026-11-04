using Application.IServices;
using Application.Services.Shop2;
using Microsoft.Extensions.DependencyInjection;

namespace Application.Modules
{
    /// <summary>
    /// DI registrations for the phase-3 storefront helpers (browsable catalogue
    /// shapes + order→Whish bridge). Application.DependencyInjection calls this once.
    /// </summary>
    public static class Shop2Module
    {
        public static IServiceCollection AddShop2Module(this IServiceCollection services)
        {
            services.AddScoped<IShop2Service, Shop2Service>();
            return services;
        }
    }
}
