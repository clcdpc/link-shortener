using Clc.Authentication;
using Clc.LinkShortener.Data;
using Clc.LinkShortener.Notes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Identity.Web.UI;

namespace Clc.LinkShortener
{
    public class Startup
    {
        public Startup(IWebHostEnvironment env)
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(env.ContentRootPath)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true)
                .AddJsonFile(@"config\settings.json", optional: false)              
                .AddJsonFile(@$"config\settings.{env.EnvironmentName}.json", optional: true)
                .AddEnvironmentVariables();

            Configuration = builder.Build();            
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            const string databaseServerKey = "Clc:DatabaseServer";
            var databaseServer = Configuration[databaseServerKey];
            if (string.IsNullOrWhiteSpace(databaseServer))
            {
                databaseServer = Configuration["Clc:Database:Hostname"];
                Configuration[databaseServerKey] = databaseServer;
            }

            services.AddClcAzureAdAuth(Configuration);

            services.AddControllersWithViews(options =>
            {
                var policy = new AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .Build();
                options.Filters.Add(new AuthorizeFilter(policy));
            });

            services.AddRazorPages()
                 .AddMicrosoftIdentityUI();

            services.AddAuthorization(options =>
            {
                options.AddPolicy("IsClcUser", policy => policy.Requirements.Add(new IsClcUserRequirement()));
                options.DefaultPolicy = options.GetPolicy("IsClcUser");
            });

            services.AddSingleton(Configuration);
            services.AddSingleton(NoteWords.Load());
            var clientIp = new ClientIp(Configuration);
            services.AddSingleton(clientIp);
            services.AddRateLimiter(options => NoteRequests.ConfigureLimits(options, clientIp));
            services.AddScoped(_ => new LinkShortenerDb(databaseServer));
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
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

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllerRoute(
                    name: "default",
                    pattern: "{controller=Link}/{action=Index}/{id?}");

                endpoints.MapControllerRoute("Follow ShortUrl", pattern: "{shortUrl}", defaults: new { controller = "Link", action = "Follow" });
                endpoints.MapControllerRoute("View Note Warning", pattern: "note/{noteId}", defaults: new { controller = "Note", action = "Warning" });

                endpoints.MapRazorPages();
            });
        }
    }
}
