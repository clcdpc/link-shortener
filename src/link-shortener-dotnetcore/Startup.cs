using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Clc.Auth.AzureAd;
using Clc.Auth.AzureAd.Security;
using Clc.Auth.AzureAd.Data;
using Clc.LinkShortener.DotNetCore.Data;
using Clc.Configuration;
using System.Threading;

namespace Clc.LinkShortener.DotNetCore
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

            services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
                .AddMicrosoftIdentityWebApp(Configuration.GetSection("AzureAd"));

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
            services.AddSingleton(x => Configuration.GetSection("Clc").Get<AppSettings>());

            services.AddScoped(x => new AuthDbHelper(x.GetRequiredService<AppSettings>()));
            services.AddScoped(x => new LinksDbHelper(x.GetRequiredService<AppSettings>()));

            services.AddSingleton<IAuthorizationHandler>(x => new IsClcUserCheckHandler(x.GetRequiredService<AppSettings>().Database.Hostname, x.GetRequiredService<AppSettings>().ApplicationName));
            services.AddSingleton<IClaimsTransformation>(x => new ClcAzureAdClaimsTransformer(x.GetRequiredService<AppSettings>().Database.Hostname, x.GetRequiredService<AppSettings>().ApplicationName));           
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
            app.UseHttpsRedirection();
            app.UseStaticFiles();

            app.UseRouting();

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
