using Application.IServices;
using Application.Services.Payments2;
using Application.Services.TillControls;
using Microsoft.Extensions.DependencyInjection;

namespace Application.Modules
{
    /// <summary>
    /// DI registrations for the Payments2 module (Whish Collect against open
    /// invoices from the till, self-order, kiosk and the customer display) and
    /// the TillControls customer-display state. Application.DependencyInjection
    /// calls this once.
    /// </summary>
    public static class PaymentsModule
    {
        public static IServiceCollection AddPaymentsModule(this IServiceCollection services)
        {
            services.AddScoped<IWhishCollectClient, WhishCollectClient>();
            services.AddScoped<IWhishInvoicePaymentService, WhishInvoicePaymentService>();
            services.AddScoped<IDisplayStateService, DisplayStateService>();
            return services;
        }
    }
}
