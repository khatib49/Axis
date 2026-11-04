using Application.Services.Phase5;
using Microsoft.Extensions.DependencyInjection;

namespace Application.Modules
{
    /// <summary>
    /// Phase 5: tournaments, station booking, ordering from the station, owner assistant
    /// on Telegram / WhatsApp, parent wallet, owner alerts, AI tools (forecast, menu import,
    /// invoice scan), setup checklist and accounting exports.
    /// </summary>
    public static class Phase5Module
    {
        public static IServiceCollection AddPhase5Module(this IServiceCollection services)
        {
            services.AddScoped<IOwnerNotifier, OwnerNotifier>();
            services.AddScoped<ITournamentService, TournamentService>();
            services.AddScoped<IBookingService, BookingService>();
            services.AddScoped<BookingJobs>();
            services.AddScoped<IStationOrderService, StationOrderService>();
            services.AddScoped<IOwnerBotService, OwnerBotService>();
            services.AddScoped<OwnerBotJobs>();
            services.AddScoped<ParentWalletService>();
            services.AddScoped<IParentWalletService>(sp => sp.GetRequiredService<ParentWalletService>());
            services.AddScoped<IParentSpendGuard>(sp => sp.GetRequiredService<ParentWalletService>());
            services.AddScoped<IAlertService, AlertService>();
            services.AddScoped<Phase5Jobs>();
            services.AddScoped<IAiToolsService, AiToolsService>();
            services.AddScoped<ISetupService, SetupService>();
            services.AddScoped<IAccountingExportService, AccountingExportService>();
            return services;
        }
    }
}
