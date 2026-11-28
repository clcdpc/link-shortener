using System;
using System.IO;
using Clc.Authentication;
using Clc.Cloudflare.AspNetCore;
using Clc.Configuration;
using Clc.LinkShortener.Data;
using Clc.LinkShortener.Notes;
using Clc.Logging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Web.UI;

namespace Clc.LinkShortener;

public static class Program
{
    internal const string ApplicationDirectoryName = "clc-link-shortener";

    public static int Main(string[] args)
    {
        if (ClcConfigurationCommandLine.TryHandle(args, ApplicationDirectoryName, out var exitCode))
            return exitCode;

        try
        {
            var builder = WebApplication.CreateBuilder(args);
            builder.AddClcConfigurationWithBootstrap(ApplicationDirectoryName);
            AddServices(builder);
            var app = BuildApplication(builder);
            app.Run();
            return 0;
        }
        catch (ClcConfigurationException error)
        {
            Console.Error.WriteLine(error.Message); // The package redacts operational values.
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"Link Shortener stopped ({error.GetType().Name}). Check application.json, directory permissions and the application log.");
        }
        return 1;
    }

    internal static void AddServices(WebApplicationBuilder builder)
    {
        builder.Logging.ClearProviders();
        // MVC trace can include bound arguments, and request diagnostics can include note URLs.
        builder.AddClcLogging(options => options.ConfigureNLog = logging =>
            logging.LoggingRules.Insert(0, new NLog.Config.LoggingRule
            {
                LoggerNamePattern = "Microsoft.AspNetCore.*",
                FinalMinLevel = NLog.LogLevel.Warn
            }));

        var config = builder.Configuration;
        const string databaseServerKey = "Clc:DatabaseServer";
        if (string.IsNullOrWhiteSpace(config[databaseServerKey]))
            config[databaseServerKey] = config["Clc:Database:Hostname"];
        var databaseServer = config[databaseServerKey];

        var services = builder.Services;
        services.AddClcAzureAdAuth(config);
        services.AddControllersWithViews(options =>
        {
            var policy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
            options.Filters.Add(new AuthorizeFilter(policy));
        });
        services.AddRazorPages().AddMicrosoftIdentityUI();
        services.AddAuthorization(options =>
        {
            options.AddPolicy("IsClcUser", policy => policy.Requirements.Add(new IsClcUserRequirement()));
            options.DefaultPolicy = options.GetPolicy("IsClcUser");
        });

        services.AddSingleton(NoteWords.Load());
        services.AddClcCloudflare(config);
        services.AddRateLimiter(NoteRequests.ConfigureLimits);
        services.AddScoped(_ => new LinkShortenerDb(databaseServer));

        var keys = new DirectoryInfo(Path.Combine(builder.GetClcApplicationPaths().ApplicationDataDirectory, "Keys"));
        var protection = services.AddDataProtection()
            .SetApplicationName(ApplicationDirectoryName)
            .PersistKeysToFileSystem(keys);
        if (OperatingSystem.IsWindows())
            protection.ProtectKeysWithDpapi(protectToLocalMachine: false);
    }

    internal static WebApplication BuildApplication(WebApplicationBuilder builder)
    {
        var app = builder.Build();
        app.UseClcCloudflare();

        if (app.Environment.IsDevelopment())
            app.UseDeveloperExceptionPage();
        else
        {
            app.UseExceptionHandler("/Home/Error");
            app.UseHsts();
        }
        app.UseWhen(context => NoteRequests.IsNotePath(context.Request.Path), notes =>
        {
            notes.Use(NoteRequests.SetPrivacyHeaders);
            notes.UseExceptionHandler(errors => errors.Run(context =>
            {
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                context.Response.ContentType = "text/plain; charset=utf-8";
                return context.Response.WriteAsync("The note request could not be completed.");
            }));
        });

        app.UseHttpsRedirection();
        app.UseStaticFiles();
        app.UseRouting();
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapControllerRoute("default", "{controller=Link}/{action=Index}/{id?}");
        app.MapControllerRoute("Follow ShortUrl", "{shortUrl}", new { controller = "Link", action = "Follow" });
        app.MapControllerRoute("View Note Warning", "note/{noteId}", new { controller = "Note", action = "Warning" });
        app.MapRazorPages();
        return app;
    }
}
