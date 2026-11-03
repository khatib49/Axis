using Microsoft.Extensions.DependencyInjection;

namespace Application.Modules
{
    /// <summary>
    /// DI registrations for the Promotions module. Filled in by the module itself;
    /// Application.DependencyInjection only calls this once.
    /// </summary>
    public static class PromotionsModule
    {
        public static IServiceCollection AddPromotionsModule(this IServiceCollection services)
        {
            return services;
        }
    }
}
