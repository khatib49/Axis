using Application.IServices;
using Application.Services.Floor;
using Microsoft.Extensions.DependencyInjection;

namespace Application.Modules
{
    /// <summary>
    /// DI registrations for the Floor module (floor plan, table sessions,
    /// courses / splits / tips, reservations). Application.DependencyInjection
    /// only calls this once.
    /// </summary>
    public static class FloorModule
    {
        public static IServiceCollection AddFloorModule(this IServiceCollection services)
        {
            services.AddScoped<IFloorService, FloorService>();
            services.AddScoped<ITableSessionService, TableSessionService>();
            services.AddScoped<IFloorBillingService, FloorBillingService>();
            services.AddScoped<IReservationService, ReservationService>();
            return services;
        }
    }
}
