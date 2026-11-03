using Microsoft.Extensions.DependencyInjection;

namespace Application.Modules
{
    /// <summary>
    /// DI registrations for the Floor module. Filled in by the module itself;
    /// Application.DependencyInjection only calls this once.
    /// </summary>
    public static class FloorModule
    {
        public static IServiceCollection AddFloorModule(this IServiceCollection services)
        {
            return services;
        }
    }
}
