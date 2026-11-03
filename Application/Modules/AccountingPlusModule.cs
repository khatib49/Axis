using Microsoft.Extensions.DependencyInjection;

namespace Application.Modules
{
    /// <summary>
    /// DI registrations for the AccountingPlus module. Filled in by the module itself;
    /// Application.DependencyInjection only calls this once.
    /// </summary>
    public static class AccountingPlusModule
    {
        public static IServiceCollection AddAccountingPlusModule(this IServiceCollection services)
        {
            return services;
        }
    }
}
