using Application.IServices;
using Application.Services.Menu4D;
using Microsoft.Extensions.DependencyInjection;

namespace Application.Modules
{
    /// <summary>4D menu: AI-labelled dish parts and AI photo-to-3D models for "see it on your table".</summary>
    public static class Menu4DModule
    {
        public static IServiceCollection AddMenu4DModule(this IServiceCollection services)
        {
            services.AddScoped<IPhotoTo3DClient, MeshyClient>();
            services.AddScoped<IMenu4DService, Menu4DService>();
            services.AddScoped<Menu4DJobs>();
            return services;
        }
    }
}
