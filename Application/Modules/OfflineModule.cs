using Application.IServices;
using Application.Services.Offline;
using Microsoft.Extensions.DependencyInjection;

namespace Application.Modules
{
    /// <summary>
    /// DI registrations for the Offline module (till resilience: idempotent
    /// replay of queued orders + cacheable menu snapshot).
    /// </summary>
    public static class OfflineModule
    {
        public static IServiceCollection AddOfflineModule(this IServiceCollection services)
        {
            services.AddScoped<IOfflineSyncService, OfflineSyncService>();
            services.AddScoped<IOfflineSnapshotService, OfflineSnapshotService>();
            return services;
        }
    }
}
