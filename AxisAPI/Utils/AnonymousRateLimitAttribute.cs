using System.Collections.Concurrent;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AxisAPI.Utils
{
    /// <summary>
    /// Simple in-memory per-IP limiter for the anonymous ordering endpoints
    /// (self-order, kiosk, aggregator webhook). Fixed window: at most
    /// MaxRequests per WindowSeconds per client IP, otherwise 429.
    /// State is process-local (one node); good enough to stop a phone
    /// hammering the API, not a DDoS shield.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
    public sealed class AnonymousRateLimitAttribute : ActionFilterAttribute
    {
        private sealed class Bucket
        {
            public long WindowStartTicks;
            public int Count;
        }

        private static readonly ConcurrentDictionary<string, Bucket> Buckets = new();
        private static long _lastSweepTicks = DateTime.UtcNow.Ticks;

        public int MaxRequests { get; set; } = 30;
        public int WindowSeconds { get; set; } = 60;

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            var ip = ResolveIp(context.HttpContext);
            var now = DateTime.UtcNow.Ticks;
            var window = TimeSpan.FromSeconds(WindowSeconds).Ticks;

            // One counter per client IP *and endpoint*: browsing one guest page must not
            // use up another endpoint's allowance (a shared counter blocked bookings after
            // a few availability look-ups, and guests on one venue Wi-Fi share an IP).
            var key = $"{ip}|{context.ActionDescriptor.DisplayName}";
            var bucket = Buckets.GetOrAdd(key, _ => new Bucket { WindowStartTicks = now, Count = 0 });
            bool limited;
            lock (bucket)
            {
                if (now - bucket.WindowStartTicks >= window)
                {
                    bucket.WindowStartTicks = now;
                    bucket.Count = 0;
                }
                bucket.Count++;
                limited = bucket.Count > MaxRequests;
            }

            Sweep(now, window);

            if (limited)
            {
                context.HttpContext.Response.Headers["Retry-After"] = WindowSeconds.ToString();
                context.Result = new ObjectResult(new { success = false, error = "RateLimited", message = "Too many requests. Please slow down." })
                {
                    StatusCode = StatusCodes.Status429TooManyRequests,
                };
                return;
            }
            base.OnActionExecuting(context);
        }

        private static void Sweep(long now, long window)
        {
            // Drop stale buckets every few minutes so the dictionary cannot grow forever.
            var last = Interlocked.Read(ref _lastSweepTicks);
            if (now - last < TimeSpan.FromMinutes(5).Ticks) return;
            if (Interlocked.CompareExchange(ref _lastSweepTicks, now, last) != last) return;
            foreach (var kv in Buckets)
                if (now - kv.Value.WindowStartTicks >= window * 2)
                    Buckets.TryRemove(kv.Key, out _);
        }

        private static string ResolveIp(HttpContext http)
        {
            var fwd = http.Request.Headers["X-Forwarded-For"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(fwd))
            {
                var first = fwd.Split(',')[0].Trim();
                if (first.Length > 0) return first;
            }
            return http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        }
    }
}
