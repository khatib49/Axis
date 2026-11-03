using Microsoft.Extensions.DependencyInjection;

namespace Application.Modules
{
    /// <summary>
    /// DI registrations for the Offline module. Filled in by the module itself;
    /// Application.DependencyInjection only calls this once.
    /// </summary>
    public static class OfflineModule
    {
        public static IServiceCollection AddOfflineModule(this IServiceCollection services)
        {
            return services;
        }
    }
}
