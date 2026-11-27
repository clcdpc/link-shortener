using System;
using System.Linq;
using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace Clc.LinkShortener.DotNetCore
{
    public sealed class ClientIp
    {
        private readonly IPNetwork[] trustedProxies;
        private readonly string headerName;

        public ClientIp(IConfiguration configuration)
        {
            headerName = configuration["ClientIp:ForwardedHeaderName"] ?? "CF-Connecting-IP";
            trustedProxies = (configuration.GetSection("ClientIp:TrustedProxyNetworks").Get<string[]>() ?? [])
                .Select(IPNetwork.Parse)
                .ToArray();

            if (trustedProxies.Any(network => network.PrefixLength == 0))
            {
                throw new InvalidOperationException("ClientIp:TrustedProxyNetworks must identify specific proxy networks.");
            }
        }

        public string Resolve(HttpContext context)
        {
            var peer = context.Connection.RemoteIpAddress;
            if (peer == null)
            {
                return "unknown";
            }

            peer = Normalize(peer);
            if (trustedProxies.Any(network => network.Contains(peer))
                && context.Request.Headers.TryGetValue(headerName, out var values)
                && values.Count == 1
                && IPAddress.TryParse(values[0], out var forwarded))
            {
                return Normalize(forwarded).ToString();
            }

            return peer.ToString();
        }

        private static IPAddress Normalize(IPAddress ip) => ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip;
    }
}
