using Microsoft.Extensions.DependencyInjection;

namespace Application.Modules
{
    /// <summary>
    /// DI registrations for the OrderingChannels module. Filled in by the module itself;
    /// Application.DependencyInjection only calls this once.
    /// </summary>
    public static class OrderingChannelsModule
    {
        public static IServiceCollection AddOrderingChannelsModule(this IServiceCollection services)
        {
            return services;
        }
    }
}
