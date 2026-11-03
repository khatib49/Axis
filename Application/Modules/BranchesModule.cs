using Application.IServices;
using Application.Services.Bi;
using Application.Services.Branches;
using Microsoft.Extensions.DependencyInjection;

namespace Application.Modules
{
    /// <summary>
    /// DI registrations for the Branches module (branch registry, user↔branch
    /// assignment, consolidated dashboard) and the BI reporting sub-module
    /// (scheduled reports, SMTP sender, owner summary, Hangfire job).
    /// Application.DependencyInjection only calls this once.
    /// </summary>
    public static class BranchesModule
    {
        public static IServiceCollection AddBranchesModule(this IServiceCollection services)
        {
            services.AddScoped<IBranchService, BranchService>();

            services.AddScoped<IBiReportService, BiReportService>();
            services.AddScoped<IEmailSender, SmtpEmailSender>();
            services.AddScoped<IScheduledReportService, ScheduledReportService>();
            services.AddScoped<IOwnerSummaryService, OwnerSummaryService>();
            services.AddScoped<BiReportJobs>();
            return services;
        }
    }
}
