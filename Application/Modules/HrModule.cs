using Microsoft.Extensions.DependencyInjection;

namespace Application.Modules
{
    /// <summary>
    /// DI registrations for the Hr module. Filled in by the module itself;
    /// Application.DependencyInjection only calls this once.
    /// </summary>
    public static class HrModule
    {
        public static IServiceCollection AddHrModule(this IServiceCollection services)
        {
            return services;
        }
    }
}
