using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System.Net;

namespace Application.Middleware
{
    public class Force403Middleware
    {
        private readonly RequestDelegate _next;

        public Force403Middleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // Continue pipeline
            await _next(context);

            // If response is 401 (Unauthorized), change to 403
            if (context.Response.StatusCode == (int)HttpStatusCode.Unauthorized)
            {
                context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
            }
        }
    }

    // Extension to simplify registration
    public static class Force403MiddlewareExtensions
    {
        public static IApplicationBuilder UseForce403ForUnauthorized(this IApplicationBuilder app)
        {
            return app.UseMiddleware<Force403Middleware>();
        }
    }
}
