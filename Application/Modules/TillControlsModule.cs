using Application.IServices;
using Application.Services.TillControls;
using Microsoft.Extensions.DependencyInjection;

namespace Application.Modules
{
    /// <summary>
    /// DI registrations for the TillControls module (cashier shifts, X/Z reports,
    /// void/refund approvals, currencies and multi-currency tenders).
    /// </summary>
    public static class TillControlsModule
    {
        public static IServiceCollection AddTillControlsModule(this IServiceCollection services)
        {
            services.AddScoped<ICurrencyService, CurrencyService>();
            services.AddScoped<IShiftReportService, ShiftReportService>();
            services.AddScoped<IShiftService, ShiftService>();
            services.AddScoped<IVoidRequestService, VoidRequestService>();
            services.AddScoped<ITenderService, TenderService>();
            return services;
        }
    }
}
