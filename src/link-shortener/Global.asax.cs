using System.Threading;
using System.Web.Http;
using System.Web.Mvc;
using System.Web.Optimization;
using System.Web.Routing;
using Clc.LinkShortener.App_Start;
using Link_Shortener;

namespace Clc.LinkShortener
{
	// Note: For instructions on enabling IIS6 or IIS7 classic mode, 
	// visit http://go.microsoft.com/?LinkId=9394801

	public class MvcApplication : System.Web.HttpApplication
	{
		protected void Application_Start()
		{
			AreaRegistration.RegisterAllAreas();

			WebApiConfig.Register(GlobalConfiguration.Configuration);
			FilterConfig.RegisterGlobalFilters(GlobalFilters.Filters);
			RouteConfig.RegisterRoutes(RouteTable.Routes);
			BundleConfig.RegisterBundles(BundleTable.Bundles);
		}

		public static void RegisterRoutes(RouteCollection routes)
		{
			routes.IgnoreRoute("{resource}.axd/{*pathInfo}");

			routes.IgnoreRoute("favicon.ico");

			routes.Clear();
			
			//routes.MapRoute(
			//	"Follow", // Route name
			//	"{shortURL}", // URL with parameters
			//	new { controller = "Home", action = "Follow" } // Parameter defaults
			//);

			routes.MapRoute(
				"Default", // Route name
				"{action}", // URL with parameters
				new { controller = "Home", action = "Index" } // Parameter defaults
			);

			routes.MapRoute(
				name: "Redirect",
				url: "{shortUrl}",
				defaults: new { controller = "Home", action = "test" }
				);

			routes.MapRoute(
				"Default", // Route name
				"{controller}/{action}/{id}", // URL with parameters
				new { controller = "Home", action = "Index", id = UrlParameter.Optional } // Parameter defaults
			);
		}
	}
}