using Application.IServices;
using Application.Services.Retail;
using Microsoft.Extensions.DependencyInjection;

namespace Application.Modules
{
    /// <summary>
    /// DI registrations for the Retail module (barcodes, label data, serial
    /// numbers). Application.DependencyInjection calls AddRetailModule once.
    /// </summary>
    public static class RetailModule
    {
        public static IServiceCollection AddRetailModule(this IServiceCollection services)
        {
            services.AddScoped<IBarcodeService, BarcodeService>();
            services.AddScoped<ISerialNumberService, SerialNumberService>();
            return services;
        }
    }
}
