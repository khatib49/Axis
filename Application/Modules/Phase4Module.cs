using Application.IServices;
using Application.Services.Phase4;
using Microsoft.Extensions.DependencyInjection;

namespace Application.Modules
{
    /// <summary>
    /// Phase 4: 3D menu, upsells, connected delivery apps and card terminals.
    /// Application.DependencyInjection calls AddPhase4Module once.
    /// </summary>
    public static class Phase4Module
    {
        public static IServiceCollection AddPhase4Module(this IServiceCollection services)
        {
            services.AddScoped<IItemModel3DService, ItemModel3DService>();
            services.AddScoped<IUpsellService, UpsellService>();
            services.AddScoped<UpsellJobs>();
            services.AddScoped<IDeliveryPlatformService, DeliveryPlatformService>();
            services.AddScoped<DeliveryPlatformJobs>();
            services.AddScoped<ICardTerminalProvider, ManualTerminalProvider>();
            services.AddScoped<ICardTerminalProvider, SimulatorTerminalProvider>();
            services.AddScoped<ICardTerminalProvider, CloudHttpTerminalProvider>();
            services.AddScoped<ICardTerminalService, CardTerminalService>();
            return services;
        }
    }
}
