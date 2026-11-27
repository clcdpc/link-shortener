using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Clc.LinkShortener.DotNetCore;
using Clc.LinkShortener.DotNetCore.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LinkShortener.Tests;

internal sealed class TestSite : IDisposable
{
    private readonly IHost host;

    public TestSite(LinksDbHelper database = null)
    {
        host = new HostBuilder()
            .UseEnvironment("Testing")
            .ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Warning))
            .ConfigureWebHost(webHost => webHost
                .UseTestServer()
                .UseContentRoot(AppContext.BaseDirectory)
                .UseStartup<Startup>()
                .ConfigureTestServices(services =>
                {
                    services.RemoveAll<IClaimsTransformation>();
                    services.AddSingleton<IClaimsTransformation, IdentityClaims>();
                    services.AddAuthentication(options => options.DefaultScheme = TestIdentity.SchemeName)
                        .AddScheme<AuthenticationSchemeOptions, TestIdentity>(TestIdentity.SchemeName, _ => { });
                    services.PostConfigure<AuthorizationOptions>(options =>
                        options.DefaultPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
                    services.AddSingleton<IStartupFilter, TestPeer>();
                    if (database != null)
                    {
                        services.Replace(ServiceDescriptor.Singleton(database));
                    }
                }))
            .Start();

        Client = host.GetTestClient();
        Client.BaseAddress = new Uri("https://localhost");
    }

    public HttpClient Client { get; }

    public async Task<(string Cookie, string Token)> AntiforgeryAsync(string path)
    {
        using var response = await Client.GetAsync(path);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        var input = Regex.Match(html, @"name=""__RequestVerificationToken""[^>]*value=""([^""]+)""");
        Assert.True(input.Success, "The form must contain an antiforgery token.");
        var cookie = response.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith(".AspNetCore.Antiforgery.", StringComparison.Ordinal))
            .Split(';', 2)[0];
        return (cookie, WebUtility.HtmlDecode(input.Groups[1].Value));
    }

    public async Task<HttpResponseMessage> PostAsync(string path, (string Cookie, string Token) antiforgery,
        params (string Name, string Value)[] fields)
    {
        var form = fields.Select(field => KeyValuePair.Create(field.Name, field.Value))
            .Append(KeyValuePair.Create("__RequestVerificationToken", antiforgery.Token));
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new FormUrlEncodedContent(form) };
        request.Headers.Add("Cookie", antiforgery.Cookie);
        return await Client.SendAsync(request);
    }

    public void Dispose()
    {
        Client.Dispose();
        host.Dispose();
    }

    private sealed class IdentityClaims : IClaimsTransformation
    {
        public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal) => Task.FromResult(principal);
    }

    private sealed class TestIdentity(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "TestIdentity";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-User", out var user))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = new List<Claim>
            {
                new(ClaimTypes.Name, user.ToString()),
                new("private-test-claim", "test-claim-value")
            };
            if (Request.Headers.ContainsKey("X-Test-Admin"))
            {
                claims.Add(new Claim(ClaimTypes.Role, "Admin"));
            }

            var identity = new ClaimsIdentity(claims, SchemeName);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }

    private sealed class TestPeer : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, proceed) =>
            {
                context.Connection.RemoteIpAddress = context.Request.Headers.TryGetValue("X-Test-Peer", out var peer)
                    ? IPAddress.Parse(peer.ToString())
                    : IPAddress.Parse("203.0.113.10");
                return proceed(context);
            });
            next(app);
        };
    }
}
