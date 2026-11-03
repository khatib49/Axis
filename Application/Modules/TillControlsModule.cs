using Microsoft.Extensions.DependencyInjection;

namespace Application.Modules
{
    /// <summary>
    /// DI registrations for the TillControls module. Filled in by the module itself;
    /// Application.DependencyInjection only calls this once.
    /// </summary>
    public static class TillControlsModule
    {
        public static IServiceCollection AddTillControlsModule(this IServiceCollection services)
        {
            return services;
        }
    }
}
