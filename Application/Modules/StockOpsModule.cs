using Microsoft.Extensions.DependencyInjection;

namespace Application.Modules
{
    /// <summary>
    /// DI registrations for the StockOps module. Filled in by the module itself;
    /// Application.DependencyInjection only calls this once.
    /// </summary>
    public static class StockOpsModule
    {
        public static IServiceCollection AddStockOpsModule(this IServiceCollection services)
        {
            return services;
        }
    }
}
