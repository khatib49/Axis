using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.Primitives;
using Serilog.Context;
using System.Security.Cryptography;

namespace AxisAPI.Middleware;

public sealed class SerilogRequestEnricherMiddleware
{
    private readonly RequestDelegate _next;
    public SerilogRequestEnricherMiddleware(RequestDelegate next) => _next = next;

    public async Task Invoke(HttpContext ctx)
    {
        // Ensure/propagate Request Id
        var reqId = EnsureRequestId(ctx);

        // Try to get controller/action if routing has already selected an endpoint
        string? controller = null, action = null;
        var endpoint = ctx.GetEndpoint();
        if (endpoint is not null)
        {
            var cad = endpoint.Metadata.GetMetadata<ControllerActionDescriptor>();
            controller = cad?.ControllerName;
            action = cad?.ActionName;
        }

        using (LogContext.PushProperty("ReqId", reqId))
        using (LogContext.PushProperty("Method", ctx.Request.Method))
        using (LogContext.PushProperty("Path", ctx.Request.Path.Value ?? "/"))
        using (LogContext.PushProperty("UserName", ctx.User?.Identity?.Name ?? "-"))
        using (LogContext.PushProperty("Controller", controller))
        using (LogContext.PushProperty("Action", action))
        {
            await _next(ctx);
            // After next(), status code is known. Push it so any logs done late (e.g., exception handler) include it.
            using (LogContext.PushProperty("StatusCode", ctx.Response.StatusCode))
            {
                // no-op; property is now on the context for late log events in the same scope
            }
        }
    }

    private static string EnsureRequestId(HttpContext ctx)
    {
        if (ctx.Request.Headers.TryGetValue("X-Request-ID", out StringValues v) && !StringValues.IsNullOrEmpty(v))
        {
            ctx.Response.Headers["X-Request-ID"] = v;
            return v!;
        }
        var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(8));
        ctx.Response.Headers["X-Request-ID"] = id;
        return id;
    }
}

// extension to register
public static class SerilogRequestEnricherExtensions
{
    public static IApplicationBuilder UseSerilogRequestEnricher(this IApplicationBuilder app)
        => app.UseMiddleware<AxisAPI.Middleware.SerilogRequestEnricherMiddleware>();
}
