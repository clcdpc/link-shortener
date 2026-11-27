using System.Net;
using Clc.LinkShortener.DotNetCore;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace LinkShortener.Tests;

public class ClientIpTests
{
    private readonly ClientIp clientIp = new(new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string>
        {
            ["ClientIp:TrustedProxyNetworks:0"] = "192.0.2.0/24",
            ["ClientIp:TrustedProxyNetworks:1"] = "2001:db8:1::/48"
        }).Build());

    [Theory]
    [InlineData("203.0.113.4", "198.51.100.5", "203.0.113.4")]
    [InlineData("::ffff:203.0.113.4", "198.51.100.5", "203.0.113.4")]
    [InlineData("192.0.2.2", "198.51.100.5", "198.51.100.5")]
    [InlineData("2001:db8:1::2", "::ffff:198.51.100.5", "198.51.100.5")]
    [InlineData("192.0.2.2", "198.51.100.5, 198.51.100.6", "192.0.2.2")]
    [InlineData("192.0.2.2", "invalid", "192.0.2.2")]
    [InlineData("192.0.2.2", null, "192.0.2.2")]
    public void ResolvesOnlySingleAddressesFromTrustedPeers(string peer, string header, string expected)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(peer);
        if (header != null)
        {
            context.Request.Headers["CF-Connecting-IP"] = header;
        }

        Assert.Equal(expected, clientIp.Resolve(context));
    }
}
