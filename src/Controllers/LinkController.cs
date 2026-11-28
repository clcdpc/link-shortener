using Clc.Authentication;
using Clc.LinkShortener.Data;
using Clc.LinkShortener.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Linq;
using System.Text;

namespace Clc.LinkShortener.Controllers
{
    [Authorize]
    public class LinkController : Controller
    {
        private readonly LinkShortenerDb db;

        public LinkController(LinkShortenerDb db)
        {
            this.db = db;
        }

        public IActionResult Index()
        {
            return View();
        }

        public IActionResult List()
        {
            var links = User.IsAdmin() ? db.GetLinks() : db.GetLinksForUser(User.Identity.Name);
            return View(links);
        }

        [HttpGet]
        public ActionResult Edit(string shortUrl)
        {
            var link = db.GetShortUrl(shortUrl);
            if (link == null) return View("InvalidShortURL");
            if (!CanEdit(link)) return Forbid();

            return View(link);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Edit(string shortUrl, string url)
        {
            var link = db.GetShortUrl(shortUrl);
            if (link == null) return RedirectToAction("List");
            if (!CanEdit(link)) return Forbid();
            if (string.IsNullOrWhiteSpace(url)) return BadRequest();

            if (!url.Contains(@"://")) url = string.Format(@"http://{0}", url);
            db.UpdateLink(shortUrl, url);
            return RedirectToAction("List");
        }

        private bool CanEdit(Link link) => string.Equals(link.Creator, User.Identity.Name) || User.IsAdmin();

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult CreateLink(Link link)
        {
            if (link.URL == null)
            {
                ModelState.AddModelError(string.Empty, "You must specify a url to shorten. Please try again.");
                return View("Index", link);
            }

            if (!string.IsNullOrEmpty(link.ShortURL) && !string.IsNullOrWhiteSpace(link.ShortURL))
            {
                if (link.ShortURL.Any(c => (!char.IsLetterOrDigit(c) && !c.Equals('-')) || char.IsWhiteSpace(c)))
                {
                    ModelState.AddModelError(string.Empty, "Short urls cannot contain special characters or spaces. Please try again.");
                    return View("Index", link);
                }

                if (db.GetShortUrl(link.ShortURL) != null)
                {
                    ModelState.AddModelError(string.Empty, "The specified short url already exists. Please try again.");
                    return View("Index", link);
                }
            }

            if (!link.URL.Contains(@"://")) link.URL = string.Format(@"http://{0}", link.URL);

            link.Creator = User.Identity.Name;
            link.ShortURL = string.IsNullOrWhiteSpace(link.ShortURL) ? GenerateShortURL() : link.ShortURL;

            db.InsertShortUrl(link);

            var fullLink = string.Format("http://shorturl.clcohio.org/{0}", link.ShortURL);

            return RedirectToAction("LinkCreated", link);
        }

        public ActionResult LinkCreated(Link link)
        {
            return View(link);
        }

        [AllowAnonymous]
        public ActionResult Follow(string shortUrl)
        {
            var link = db.GetShortUrl(shortUrl);
            if (link == null) return View("InvalidShortURL");
            db.AddLinkVisit(shortUrl, HttpContext.GetClientIp(), Request.GetTypedHeaders().Referer?.ToString());
            return Redirect(link.URL);
        }

        private string GenerateShortURL()
        {
            var rand = new Random();
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz1234567890";
            var linkBuilder = new StringBuilder();

            for (int i = 0; i < 5; i++)
            {
                linkBuilder.Append(chars[rand.Next(chars.Length)]);
            }

            var shortURL = linkBuilder.ToString();


            if (db.GetShortUrl(shortURL) != null)
            {
                shortURL = GenerateShortURL();
            }

            return shortURL;
        }
    }
}
