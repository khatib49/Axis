using Application.IServices;
using Application.Services.Marketing;
using Microsoft.Extensions.DependencyInjection;

namespace Application.Modules
{
    /// <summary>
    /// DI registrations for the Marketing module (segments, campaigns over
    /// e-mail / SMS / WhatsApp, unsubscribe list, Hangfire job). IEmailSender and
    /// IWhatsAppService are registered elsewhere (Bi / Ai) and reused here.
    /// Application.DependencyInjection only calls this once.
    /// </summary>
    public static class MarketingModule
    {
        public static IServiceCollection AddMarketingModule(this IServiceCollection services)
        {
            services.AddScoped<ISmsSender, HttpSmsSender>();
            services.AddScoped<IMarketingUnsubscribeService, MarketingUnsubscribeService>();
            services.AddScoped<IMarketingSegmentService, MarketingSegmentService>();
            services.AddScoped<IMarketingCampaignService, MarketingCampaignService>();
            services.AddScoped<MarketingJobs>();
            return services;
        }
    }
}
