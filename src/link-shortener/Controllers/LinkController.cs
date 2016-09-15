using System;
using System.Web.Mvc;
using System.Linq;
using Clc.LinkShortener.Data;

namespace Clc.LinkShortener.Controllers
{
    [Authorize]
	public class LinkController : Controller
	{
		private LinkEntities db;

		public LinkController()
		{
			db = new LinkEntities();
		}

		public ActionResult Index()
		{			
			return View();
		}

        public ActionResult List()
        {
            var user = User.Identity.Name;

            var links = db.Admins.Any(a => a.Username == user) ? db.Links : db.Links.Where(l => l.Creator == user);
            return View(links);
        }

        public ActionResult Edit(string shortUrl)
        {
            var link = db.Links.SingleOrDefault(s => s.ShortURL == shortUrl);
            if (link == null) return View("InvalidShortURL");

            return View(link);
        }

        [HttpPost]
        public ActionResult Edit(Link link)
        {
            var _link = db.Links.Single(l => l.ShortURL == link.ShortURL);

            if (!link.URL.Contains(@"://")) link.URL = string.Format(@"http://{0}", link.URL);
            _link.URL = link.URL;
            _link.ModifiedDate = DateTime.Now;

            db.SaveChanges();

            return RedirectToAction("List");
        }

		public ActionResult CreateLink(Link link)
		{
			var utils = new LinkUtils();

			if (link.URL == null)
			{
				ModelState.AddModelError(string.Empty, "You must specify a url to shorten. Please try again.");
				return View("Index", link);
			}

			if (!string.IsNullOrEmpty(link.ShortURL) && !string.IsNullOrWhiteSpace(link.ShortURL))
			{
				if (link.ShortURL.Any(c => !char.IsLetterOrDigit(c) || char.IsWhiteSpace(c)))
				{
					ModelState.AddModelError(string.Empty, "Short urls cannot contain special characters or spaces. Please try again.");
					return View("Index", link);
				}

				if (db.Links.Any(l => l.ShortURL == link.ShortURL))
				{
					ModelState.AddModelError(string.Empty,"The specified short url already exists. Please try again.");
					return View("Index", link);
				}
			}

			if (!link.URL.Contains(@"://")) link.URL = string.Format(@"http://{0}", link.URL);

			link.Creator = string.IsNullOrEmpty(User.Identity.Name) ? LinkUtils.VisitorIP : User.Identity.Name;
			link.CreationDate = DateTime.Now;
            link.ShortURL = string.IsNullOrWhiteSpace(link.ShortURL) ? utils.GenerateShortURL() : link.ShortURL;
			db.Links.Add(link);
			db.SaveChanges();

			var fullLink = string.Format("http://shorturl.clcohio.org/{0}",link.ShortURL);

			link.LinkVisits = null;
			return RedirectToAction("LinkCreated", link);
		}

		public ActionResult LinkCreated(Link link)
		{
			link.LinkVisits = null;
			return View(link);
		}

        [AllowAnonymous]
		public ActionResult Follow(string shortUrl)
		{
			var link = db.Links.SingleOrDefault(l => l.ShortURL == shortUrl);
            if (link == null) return View("InvalidShortURL");

			var visit = new LinkVisit
				            {
					            VisitorIP = LinkUtils.VisitorIP,
					            Referrer = LinkUtils.UrlReferrer,
					            VisitTime = DateTime.Now,
					            ShortURL = shortUrl
				            };
			db.LinkVisits.Add(visit);
			db.SaveChanges();

			return Redirect(link.URL);
		}
	}
}
