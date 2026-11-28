using Microsoft.AspNetCore.Http;

namespace Clc.LinkShortener
{
    internal static class HttpContextExtensions
    {
        internal static string GetClientIp(this HttpContext context)
        {
            var address = context.Connection.RemoteIpAddress;
            if (address?.IsIPv4MappedToIPv6 == true)
            {
                address = address.MapToIPv4();
            }
            return address?.ToString() ?? "unknown";
        }
    }
}
