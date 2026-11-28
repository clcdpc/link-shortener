using System.Net;
using System.Text.RegularExpressions;

namespace LinkShortener.Tests;

public class LinkHttpTests
{
    private const string OpenBookmark = "window.open('https://go.clcohio.org/?url='+encodeURIComponent(location.href),'_blank','noopener')";

    [Theory]
    [InlineData(null)]
    [InlineData("https://example.test/a%2Fb?q=1&next=x%26y+z#part%202")]
    public async Task HomeShowsBookmarkletAndPrefillsOnlyTheDestination(string destination)
    {
        using var site = new TestSite();
        var path = destination == null ? "/" : "/?url=" + Uri.EscapeDataString(destination);
        using var anonymous = await site.Client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        site.Client.DefaultRequestHeaders.Add("X-Test-User", "link.author");
        var html = await site.Client.GetStringAsync(path);
        Assert.Contains($"href=\"javascript:void({OpenBookmark})\"", html);
        Assert.Contains("action=\"/Link/CreateLink\"", html);
        Assert.Equal(destination ?? "", InputValue(html, "URL"));
        Assert.Equal("", InputValue(html, "ShortURL"));
    }

    [Fact]
    public async Task LegacyScriptCanBeLoadedWithoutSigningIn()
    {
        using var site = new TestSite();
        var script = await site.Client.GetStringAsync("/scripts/bookmarklet.js");
        Assert.Equal(OpenBookmark + ";", script.Trim().Replace(" ", ""));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("https://")]
    [InlineData("https://bad host/page")]
    [InlineData("http://example.test:99999")]
    [InlineData("https://example.test/%zz")]
    [InlineData("javascript:alert(1)")]
    [InlineData("javascript:80")]
    [InlineData("file:///tmp/example")]
    [InlineData("ftp://example.test")]
    [InlineData("data:text/plain,example")]
    public async Task InvalidCreationRedisplaysBothFieldsWithoutUsingSql(string destination)
    {
        using var site = new TestSite();
        site.Client.DefaultRequestHeaders.Add("X-Test-User", "link.author");
        var token = await site.AntiforgeryAsync("/");
        using var response = await site.PostAsync("/Link/CreateLink", token,
            ("URL", destination), ("ShortURL", "keep-me"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Enter a valid HTTP or HTTPS destination URL.", html);
        Assert.Equal(destination, InputValue(html, "URL"));
        Assert.Equal("keep-me", InputValue(html, "ShortURL"));
    }

    [Fact]
    public async Task MissingConfirmationIdentifierShowsInvalidLink()
    {
        using var site = new TestSite();
        site.Client.DefaultRequestHeaders.Add("X-Test-User", "link.author");
        var html = await site.Client.GetStringAsync("/Link/LinkCreated");
        Assert.Contains("link you're looking for is invalid", html);
    }

    internal static string InputValue(string html, string name)
    {
        var input = Regex.Match(html, $@"<input\b[^>]*\bname=""{name}""[^>]*>");
        Assert.True(input.Success, $"Expected a form field named {name}.");
        return WebUtility.HtmlDecode(Regex.Match(input.Value, @"\bvalue=""([^""]*)""").Groups[1].Value);
    }
}
