using Application.IServices;
using Application.Services.StockOps;
using Microsoft.Extensions.DependencyInjection;

namespace Application.Modules
{
    /// <summary>
    /// DI registrations for the StockOps module (locations, stocktakes,
    /// transfers, reorder suggestions, batch expiry).
    /// </summary>
    public static class StockOpsModule
    {
        public static IServiceCollection AddStockOpsModule(this IServiceCollection services)
        {
            services.AddScoped<IStockLocationService, StockLocationService>();
            services.AddScoped<IStockCountService, StockCountService>();
            services.AddScoped<IStockTransferService, StockTransferService>();
            services.AddScoped<IReorderService, ReorderService>();
            services.AddScoped<IStockBatchService, StockBatchService>();
            services.AddScoped<StockOpsJobs>();
            return services;
        }
    }
}
