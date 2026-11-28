using System.Reflection;
using Clc.Configuration;
using Clc.LinkShortener;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;

namespace LinkShortener.Tests;

internal sealed class TestApplicationDirectory : IDisposable
{
    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("link-shortener-");
    public ClcApplicationPaths Paths { get; }
    private string ContentRoot => Path.Combine(Paths.ApplicationDataDirectory, "Application");

    public TestApplicationDirectory()
    {
        // As in the Clc.Logging package tests, use the package's isolated-root overload.
        // Its public path resolver always points to system-wide application data.
        var resolve = typeof(ClcApplicationPaths)
            .GetMethod("Resolve", BindingFlags.NonPublic | BindingFlags.Static, [typeof(string), typeof(string)])
            .CreateDelegate<Func<string, string, ClcApplicationPaths>>();
        Paths = resolve(Program.ApplicationDirectoryName, root.FullName);
        Directory.CreateDirectory(Paths.ConfigurationDirectory);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "test-settings.json"), Paths.ConfigurationFilePath);
        Directory.CreateDirectory(Path.Combine(ContentRoot, "config"));
        File.Copy(Path.Combine(AppContext.BaseDirectory, "config", "example.json"), Path.Combine(ContentRoot, "config", "example.json"));
    }

    public WebApplicationBuilder CreateBuilder(params string[] args)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(Program).Assembly.GetName().Name,
            EnvironmentName = "Testing",
            ContentRootPath = ContentRoot,
            Args = args
        });
        builder.WebHost.UseTestServer();
        var bootstrap = typeof(ClcConfigurationBuilderExtensions)
            .GetMethod("AddClcConfigurationWithBootstrap", BindingFlags.NonPublic | BindingFlags.Static)
            .CreateDelegate<Func<IHostApplicationBuilder, ClcApplicationPaths, string, IHostApplicationBuilder>>();
        bootstrap(builder, Paths, null);
        return builder;
    }

    public void Dispose() => root.Delete(recursive: true);
}
