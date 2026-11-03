using Application.IServices;
using Application.Services.Hr;
using Microsoft.Extensions.DependencyInjection;

namespace Application.Modules
{
    /// <summary>
    /// DI registrations for the Hr module. Filled in by the module itself;
    /// Application.DependencyInjection only calls this once.
    /// </summary>
    public static class HrModule
    {
        public static IServiceCollection AddHrModule(this IServiceCollection services)
        {
            services.AddScoped<IHrEmployeeService, HrEmployeeService>();
            services.AddScoped<IHrScheduleService, HrScheduleService>();
            services.AddScoped<IHrAttendanceService, HrAttendanceService>();
            services.AddScoped<IHrLeaveService, HrLeaveService>();
            services.AddScoped<IHrTipService, HrTipService>();
            services.AddScoped<IHrPayrollService, PayrollService>();
            return services;
        }
    }
}
