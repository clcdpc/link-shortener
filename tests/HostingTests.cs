using System.Text.Json.Nodes;
using System.Xml.Linq;
using Clc.Configuration;
using Clc.LinkShortener;
using Clc.LinkShortener.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LinkShortener.Tests;

public class HostingTests
{
    [Fact]
    public void BootstrapCreatesOnlyTheExampleAndStopsUntilConfigured()
    {
        using var directory = new TestApplicationDirectory();
        File.Delete(directory.Paths.ConfigurationFilePath);
        var created = Assert.Throws<ClcConfigurationException>(() => directory.CreateBuilder());
        Assert.Equal(ClcConfigurationErrorReason.ConfigurationCreated, created.Reason);
        Assert.Equal(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "config", "example.json")),
            File.ReadAllText(directory.Paths.ConfigurationFilePath));

        var placeholder = Assert.Throws<ClcConfigurationException>(() => directory.CreateBuilder());
        Assert.Equal(ClcConfigurationErrorReason.UnreplacedPlaceholder, placeholder.Reason);
        Assert.False(Directory.Exists(Path.Combine(directory.Paths.ApplicationDataDirectory, "Logs")));
    }

    [Fact]
    public void InvalidConfigurationStopsStartupWithARedactedDiagnostic()
    {
        using var directory = new TestApplicationDirectory();
        File.WriteAllText(directory.Paths.ConfigurationFilePath, """{"AzureAd":{"ClientSecret":"synthetic-marker"}""");
        var error = Assert.Throws<ClcConfigurationException>(() => directory.CreateBuilder());
        Assert.Equal(ClcConfigurationErrorReason.MalformedJson, error.Reason);
        Assert.DoesNotContain("synthetic-marker", error.ToString());
    }

    [Fact]
    public async Task AuthoritativeSettingsReachTheHostAndLegacyDatabaseHostnameIsSupported()
    {
        using var directory = new TestApplicationDirectory();
        var file = directory.Paths.ConfigurationFilePath;
        var config = JsonNode.Parse(File.ReadAllText(file));
        config["Clc"]["DatabaseServer"] = " ";
        config["Clc"]["Database"] = new JsonObject { ["Hostname"] = "legacy.test" };
        File.WriteAllText(file, config.ToJsonString());
        var builder = directory.CreateBuilder("--Clc:ApplicationName=ignored", "--urls=https://localhost:8001");
        Program.AddServices(builder);
        await using var app = Program.BuildApplication(builder);
        await app.StartAsync();
        Assert.Equal("link-shortener", app.Configuration["Clc:ApplicationName"]);
        Assert.Equal("legacy.test", app.Configuration["Clc:DatabaseServer"]);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LinkShortenerDb>();
        Assert.Equal("legacy.test", new SqlConnectionStringBuilder(db.ConnectionString).DataSource);
        Assert.Equal("/signin-oidc", app.Configuration["AzureAd:CallbackPath"]);
        Assert.Equal("localhost", app.Configuration["AllowedHosts"]);
        Assert.Equal("https://localhost:8001", app.Configuration["urls"]);
        var links = app.Services.GetRequiredService<LinkGenerator>();
        Assert.Equal("/MicrosoftIdentity/Account/SignedOut",
            links.GetPathByPage("/Account/SignedOut", values: new { area = "MicrosoftIdentity" }));
        using var client = app.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://excluded.invalid/note/AbC12");
        using var response = await client.SendAsync(request);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ForwardingRequiresExplicitConfiguration()
    {
        using var directory = new TestApplicationDirectory();
        var file = directory.Paths.ConfigurationFilePath;
        var config = JsonNode.Parse(File.ReadAllText(file)).AsObject();
        config.Remove("Cloudflare");
        File.WriteAllText(file, config.ToJsonString());
        var builder = directory.CreateBuilder();
        Program.AddServices(builder);
        await using var app = Program.BuildApplication(builder);
        await app.StartAsync();
        var result = await app.GetTestServer().SendAsync(context =>
        {
            context.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
            context.Request.Scheme = "https";
            context.Request.Host = new HostString("localhost");
            context.Request.Path = "/note/AbC12";
            context.Request.Headers["CF-Connecting-IP"] = "198.51.100.9";
        });
        Assert.Equal(StatusCodes.Status200OK, result.Response.StatusCode);
        Assert.Equal("127.0.0.1", result.GetClientIp());
    }

    [Fact]
    public async Task RestartReusesTheProtectedKeyRingAndWritesPrivateNoteRequestsSafely()
    {
        using var directory = new TestApplicationDirectory();
        string protectedValue;
        var file = directory.Paths.ConfigurationFilePath;
        var config = JsonNode.Parse(File.ReadAllText(file));
        config["Logging"] = new JsonObject { ["LogLevel"] = new JsonObject { ["Microsoft.AspNetCore"] = "Trace" } };
        File.WriteAllText(file, config.ToJsonString());
        var builder = directory.CreateBuilder();
        Program.AddServices(builder);
        await using (var first = Program.BuildApplication(builder))
        {
            await first.StartAsync();
            var options = first.Services.GetRequiredService<IOptions<DataProtectionOptions>>();
            Assert.Equal("clc-link-shortener", options.Value.ApplicationDiscriminator);
            protectedValue = first.Services.GetRequiredService<IDataProtectionProvider>()
                .CreateProtector("restart-test").Protect("test payload");
            using var client = first.GetTestClient();
            using var response = await client.GetAsync("https://localhost/note/private-path-sentinel");
            response.EnsureSuccessStatusCode();
            first.Logger.LogInformation("Synthetic restart check");
        }

        var keys = Path.Combine(directory.Paths.ApplicationDataDirectory, "Keys");
        var key = XDocument.Load(Assert.Single(Directory.GetFiles(keys, "key-*.xml")));
        if (OperatingSystem.IsWindows())
        {
            var encrypted = Assert.Single(key.Descendants(), element => element.Name.LocalName == "encryptedSecret");
            Assert.Contains("DpapiXmlDecryptor", (string)encrypted.Attribute("decryptorType"));
            Assert.Empty(key.Descendants("masterKey"));
        }
        var logs = Directory.GetFiles(Path.Combine(directory.Paths.ApplicationDataDirectory, "Logs"), "*.log");
        var log = string.Join("\n", logs.Select(File.ReadAllText));
        Assert.Contains("Synthetic restart check", log);
        Assert.DoesNotContain("private-path-sentinel", log);

        builder = directory.CreateBuilder();
        Program.AddServices(builder);
        await using var second = Program.BuildApplication(builder);
        await second.StartAsync();
        Assert.Equal("test payload", second.Services.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("restart-test").Unprotect(protectedValue));
    }
}
