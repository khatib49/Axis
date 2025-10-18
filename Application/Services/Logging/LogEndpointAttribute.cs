using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Serilog.Context;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace AxisAPI.Logging;

[AttributeUsage(AttributeTargets.Method)]
public sealed class LogOnErrorAttribute : Attribute, IAsyncActionFilter
{
    public int MaxPreviewChars { get; init; } = 2048;

    public async Task OnActionExecutionAsync(ActionExecutingContext ctx, ActionExecutionDelegate next)
    {
        var http = ctx.HttpContext;
        var logger = http.RequestServices.GetRequiredService<ILogger<LogOnErrorAttribute>>();

        var reqId = EnsureRequestId(http);
        var ctrl = (string?)ctx.RouteData.Values["controller"] ?? "-";
        var action = (string?)ctx.RouteData.Values["action"] ?? "-";
        var method = http.Request.Method;
        var path = http.Request.Path.Value ?? "/";
        var user = http.User?.Identity?.Name ?? "-";
        var argsPreview = ToPreview(ctx.ActionArguments, MaxPreviewChars);

        using (LogContext.PushProperty("ReqId", reqId))
        using (LogContext.PushProperty("Controller", ctrl))
        using (LogContext.PushProperty("Action", action))
        using (LogContext.PushProperty("UserName", user))
        using (LogContext.PushProperty("Method", method))
        using (LogContext.PushProperty("Path", path))
        using (LogContext.PushProperty("Args", argsPreview))
        {
            var sw = Stopwatch.StartNew();

            try
            {
                var executed = await next();
                sw.Stop();

                var (status, resultPreview) = ExtractStatusAndPreview(executed.Result, MaxPreviewChars);
                var isSuccess = status is >= 200 and < 400;

                if (!isSuccess)
                {
                    using (LogContext.PushProperty("StatusCode", status))
                    using (LogContext.PushProperty("ElapsedMs", (int)sw.Elapsed.TotalMilliseconds))
                    using (LogContext.PushProperty("ResultPreview", resultPreview))
                    {
                        // Warning for 4xx, Error for 5xx (Serilog sink captures both)
                        if (status >= 500)
                            logger.LogError("Non-success result {Status} in {Elapsed} ms", status, sw.ElapsedMilliseconds);
                        else
                            logger.LogWarning("Non-success result {Status} in {Elapsed} ms", status, sw.ElapsedMilliseconds);
                    }
                }
            }
            catch (Exception ex)
            {
                sw.Stop();
                using (LogContext.PushProperty("StatusCode", 500))
                using (LogContext.PushProperty("ElapsedMs", (int)sw.Elapsed.TotalMilliseconds))
                {
                    logger.LogError(ex, "Unhandled exception in {Controller}.{Action} after {Elapsed} ms", ctrl, action, sw.ElapsedMilliseconds);
                }
                throw; // preserve pipeline behavior
            }
        }
    }

    // ---- helpers ----
    private static (int status, object? preview) ExtractStatusAndPreview(IActionResult? result, int cap)
    {
        // Specific types first
        if (result is BadRequestObjectResult br) return (400, Preview(br.Value, cap));
        if (result is UnauthorizedObjectResult u) return (401, Preview(u.Value, cap));
        if (result is ForbidResult) return (403, null);
        if (result is NotFoundObjectResult nf) return (404, Preview(nf.Value, cap));
        if (result is ConflictObjectResult cf) return (409, Preview(cf.Value, cap));
        if (result is CreatedAtActionResult caa) return (201, Preview(caa.Value, cap));
        if (result is CreatedResult cr) return (201, Preview(cr.Value, cap));
        if (result is OkObjectResult ok) return (200, Preview(ok.Value, cap));
        if (result is StatusCodeResult sc) return (sc.StatusCode, null);

        // Base class last
        if (result is ObjectResult o) return (o.StatusCode ?? 200, Preview(o.Value, cap));

        // Unknown → assume 200
        return (200, null);
    }


    private static object? Preview(object? value, int cap)
    {
        if (value is null) return null;
        if (value is string s) return s.Length <= cap ? s : s[..cap] + "...";
        try
        {
            var json = JsonSerializer.Serialize(value);
            return json.Length <= cap ? json : json[..cap] + "...";
        }
        catch { return value.GetType().Name; }
    }

    private static object ToPreview(IDictionary<string, object?> src, int cap)
    {
        var d = new Dictionary<string, object?>(src.Count);
        foreach (var (k, v) in src) d[k] = Preview(v, cap);
        return d;
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
