using System.Net;

namespace LinkShortener.Tests;

public class NoteHttpTests
{
    [Theory]
    [InlineData("/note/AbC12")]
    [InlineData("/Note/Display/AbC12")]
    public async Task GetShowsAConfirmationFormWithoutReadingTheDatabase(string path)
    {
        using var site = new TestSite();
        using var response = await site.Client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertPrivacy(response);

        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("action=\"/Note/Display\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("method=\"post\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("name=\"id\" value=\"AbC12\"", html);
        Assert.Contains("name=\"__RequestVerificationToken\"", html);
    }

    [Theory]
    [InlineData("/Note/Display")]
    [InlineData("/Note/Create")]
    [InlineData("/Link/CreateLink")]
    [InlineData("/Link/Edit")]
    public async Task PostRejectsAnInvalidAntiforgeryToken(string path)
    {
        using var site = new TestSite();
        site.Client.DefaultRequestHeaders.Add("X-Test-User", "test.user");
        var (_, token) = await site.AntiforgeryAsync("/Link/Index");
        using var response = await site.PostAsync(path, ("ignored=value", token), ("id", "AbC12"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        if (path.StartsWith("/Note", StringComparison.Ordinal))
        {
            AssertPrivacy(response);
        }
        else
        {
            Assert.False(response.Headers.Contains("Referrer-Policy"));
        }
    }

    [Theory]
    [InlineData("/Note/Create", HttpStatusCode.OK)]
    [InlineData("/Link/CreateLink", HttpStatusCode.MethodNotAllowed)]
    public async Task GetCannotCreateRecords(string path, HttpStatusCode status)
    {
        using var site = new TestSite();
        site.Client.DefaultRequestHeaders.Add("X-Test-User", "test.user");
        using var response = await site.Client.GetAsync(path);
        Assert.Equal(status, response.StatusCode);
        if (status == HttpStatusCode.OK)
        {
            // The public note route interprets "Create" as an identifier on GET.
            Assert.Contains("<h2>Warning</h2>", await response.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task HomeRedirectsWithoutExposingClaims()
    {
        using var site = new TestSite();
        site.Client.DefaultRequestHeaders.Add("X-Test-User", "test.user");
        using var response = await site.Client.GetAsync("/Home/Index");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location?.OriginalString);
        Assert.DoesNotContain("test-claim-value", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ConcurrentConsumptionRequestsShareTheClientLimit()
    {
        using var site = new TestSite();
        var responses = await Task.WhenAll(Enumerable.Range(1, 20).Select(async index =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/Note/Display");
            request.Headers.Add("CF-Connecting-IP", $"198.51.100.{index}");
            using var response = await site.Client.SendAsync(request);
            AssertPrivacy(response);
            return response.StatusCode;
        }));

        Assert.Equal(10, responses.Count(status => status == HttpStatusCode.BadRequest));
        Assert.Equal(10, responses.Count(status => status == HttpStatusCode.TooManyRequests));
    }

    [Fact]
    public async Task ApplicationLimitCapsConsumptionAcrossClients()
    {
        using var site = new TestSite();
        for (var client = 1; client <= 31; client++)
        {
            var expected = client <= 30 ? HttpStatusCode.BadRequest : HttpStatusCode.TooManyRequests;
            for (var attempt = 0; attempt < 10; attempt++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "/Note/Display");
                request.Headers.Add("X-Test-Peer", $"192.0.2.{client}");
                using var response = await site.Client.SendAsync(request);
                Assert.Equal(expected, response.StatusCode);
            }
        }
    }

    [Fact]
    public async Task WarningsAndLegacyDisplayShareTheirOwnLimit()
    {
        using var site = new TestSite();
        for (var attempt = 0; attempt < 30; attempt++)
        {
            using var response = await site.Client.GetAsync("/note/AbC12");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        using var limited = await site.Client.GetAsync("/Note/Display/AbC12");
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        AssertPrivacy(limited);

        using var consumption = await site.Client.PostAsync("/Note/Display", null);
        Assert.Equal(HttpStatusCode.BadRequest, consumption.StatusCode);
    }

    internal static void AssertPrivacy(HttpResponseMessage response)
    {
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal("no-referrer", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
        Assert.Equal("noindex, nofollow", Assert.Single(response.Headers.GetValues("X-Robots-Tag")));
    }
}
