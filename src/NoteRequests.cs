using System;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;

namespace Clc.LinkShortener
{
    internal enum NoteRequestType
    {
        Warning,
        Consumption
    }

    [AttributeUsage(AttributeTargets.Method)]
    internal sealed class NoteRequestLimitAttribute(NoteRequestType type) : Attribute
    {
        public NoteRequestType Type { get; } = type;
    }

    internal static class NoteRequests
    {
        internal static bool IsNotePath(PathString path) => path.StartsWithSegments("/note", StringComparison.OrdinalIgnoreCase);

        internal static async Task SetPrivacyHeaders(HttpContext context, RequestDelegate next)
        {
            context.Response.OnStarting(() =>
            {
                context.Response.Headers.CacheControl = "no-store";
                context.Response.Headers["Referrer-Policy"] = "no-referrer";
                context.Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
                return Task.CompletedTask;
            });
            await next(context);
        }

        internal static void ConfigureLimits(RateLimiterOptions options, ClientIp clientIp)
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.CreateChained(
                Window(NoteRequestType.Consumption, clientIp.Resolve, 10, TimeSpan.FromMinutes(1)),
                Window(NoteRequestType.Consumption, clientIp.Resolve, 100, TimeSpan.FromHours(1)),
                Window(NoteRequestType.Consumption, _ => "application", 300, TimeSpan.FromMinutes(1)),
                Window(NoteRequestType.Warning, clientIp.Resolve, 30, TimeSpan.FromMinutes(1)));
        }

        private static PartitionedRateLimiter<HttpContext> Window(
            NoteRequestType type, Func<HttpContext, string> key, int limit, TimeSpan duration)
            => PartitionedRateLimiter.Create<HttpContext, string>(context =>
                context.GetEndpoint()?.Metadata.GetMetadata<NoteRequestLimitAttribute>()?.Type == type
                    ? RateLimitPartition.GetFixedWindowLimiter(key(context), _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = limit,
                        Window = duration,
                        QueueLimit = 0
                    })
                    : RateLimitPartition.GetNoLimiter("unlimited"));

        internal static string GetFetchSite(HttpRequest request) =>
            request.Headers["Sec-Fetch-Site"] switch
            {
                { Count: 1 } values when values[0] is "same-origin" or "same-site" or "cross-site" or "none" => values[0],
                _ => null
            };
    }
}
