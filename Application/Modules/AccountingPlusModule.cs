using Application.IServices;
using Application.Services.AccountingPlus;
using Microsoft.Extensions.DependencyInjection;

namespace Application.Modules
{
    /// <summary>
    /// DI registrations for the AccountingPlus module (bank reconciliation, supplier
    /// bills / AP aging, AR aging, VAT, fixed assets, period locking).
    /// </summary>
    public static class AccountingPlusModule
    {
        public static IServiceCollection AddAccountingPlusModule(this IServiceCollection services)
        {
            services.AddScoped<IAccountingPeriodService, AccountingPeriodService>();
            services.AddScoped<IBankReconciliationService, BankReconciliationService>();
            services.AddScoped<ISupplierBillService, SupplierBillService>();
            services.AddScoped<IReceivablesService, ReceivablesService>();
            services.AddScoped<ITaxService, TaxService>();
            services.AddScoped<IFixedAssetService, FixedAssetService>();
            return services;
        }
    }
}
