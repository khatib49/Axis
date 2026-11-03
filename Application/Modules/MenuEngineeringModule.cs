using Application.IServices;
using Application.Services.MenuEngineering;
using Microsoft.Extensions.DependencyInjection;

namespace Application.Modules
{
    /// <summary>
    /// DI registrations for the MenuEngineering module (recipe costing and the
    /// Star / Plowhorse / Puzzle / Dog matrix). Application.DependencyInjection
    /// only calls this once.
    /// </summary>
    public static class MenuEngineeringModule
    {
        public static IServiceCollection AddMenuEngineeringModule(this IServiceCollection services)
        {
            services.AddScoped<IMenuEngineeringService, MenuEngineeringService>();
            return services;
        }
    }
}
