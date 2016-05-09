using Clc.LinkShortener.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;

namespace Clc.LinkShortener.Controllers
{
    [Authorize]
    public class EditController : Controller
    {
        LinkEntities db = new LinkEntities();

        public ActionResult Index()
        {
            var user = User.Identity.Name;
            
            var links = db.Admins.Any(a => a.Username == user) ? db.Links : db.Links.Where(l => l.Creator == user);
            return View(links);
        }

        public ActionResult Link(string shortUrl)
        {
            var link = db.Links.SingleOrDefault(s => s.ShortURL == shortUrl);
            if (link == null) return View("InvalidShortURL");

            return View(link);
        }
        
        protected override void Dispose(bool disposing)
        {
            db.Dispose();
            base.Dispose(disposing);
        }

    }
}
