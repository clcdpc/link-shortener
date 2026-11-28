using Clc.LinkShortener;
using Microsoft.AspNetCore.Http;

namespace LinkShortener.Tests;

public class ClientIpTests
{
    [Fact]
    public void MissingConnectionAddressUsesUnknown() =>
        Assert.Equal("unknown", new DefaultHttpContext().GetClientIp());

    [Theory]
    [InlineData("203.0.113.4", "198.51.100.5", "203.0.113.4")]
    [InlineData("::ffff:203.0.113.4", "198.51.100.5", "203.0.113.4")]
    [InlineData("192.0.2.2", "198.51.100.5", "198.51.100.5")]
    [InlineData("2001:db8:1::2", "::ffff:198.51.100.5", "198.51.100.5")]
    [InlineData("192.0.2.2", "198.51.100.5, 198.51.100.6", "192.0.2.2")]
    [InlineData("192.0.2.2", "invalid", "192.0.2.2")]
    [InlineData("192.0.2.2", null, "192.0.2.2")]
    public async Task ResolvesOnlySingleAddressesFromTrustedPeers(string peer, string header, string expected)
    {
        using var site = new TestSite();
        var context = await site.Server.SendAsync(request =>
        {
            request.Request.Scheme = "https";
            request.Request.Path = "/note/AbC12";
            request.Request.Headers["X-Test-Peer"] = peer;
            if (header != null)
            {
                request.Request.Headers["CF-Connecting-IP"] = header;
            }
        });

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal(expected, context.GetClientIp());
    }
}
