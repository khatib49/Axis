using Microsoft.Extensions.DependencyInjection;

namespace Application.Modules
{
    /// <summary>
    /// DI registrations for the Branches module. Filled in by the module itself;
    /// Application.DependencyInjection only calls this once.
    /// </summary>
    public static class BranchesModule
    {
        public static IServiceCollection AddBranchesModule(this IServiceCollection services)
        {
            return services;
        }
    }
}
