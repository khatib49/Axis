using Application.IServices;
using Application.Services.OrderingChannels;
using Microsoft.Extensions.DependencyInjection;

namespace Application.Modules
{
    /// <summary>
    /// DI registrations for the OrderingChannels module (delivery & takeaway,
    /// aggregator webhook, QR self-ordering, kiosk). Application.DependencyInjection
    /// calls this once.
    /// </summary>
    public static class OrderingChannelsModule
    {
        public static IServiceCollection AddOrderingChannelsModule(this IServiceCollection services)
        {
            services.AddScoped<PublicMenuBuilder>();
            services.AddScoped<IDeliveryService, DeliveryService>();
            services.AddScoped<ISelfOrderService, SelfOrderService>();
            services.AddScoped<IKioskService, KioskService>();
            return services;
        }
    }
}
