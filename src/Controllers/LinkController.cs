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

        public IActionResult Index([FromQuery] string url = null)
        {
            return View(new Link { URL = url });
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
            if (!TryGetDestination(url, out var destination))
            {
                link.URL = url;
                return View(link);
            }

            db.UpdateLink(shortUrl, destination);
            return RedirectToAction("List");
        }

        private bool CanEdit(Link link) => string.Equals(link.Creator, User.Identity.Name) || User.IsAdmin();

        private bool TryGetDestination(string input, out string destination)
        {
            destination = input?.Trim();
            if (!string.IsNullOrEmpty(destination))
            {
                var colon = destination.IndexOf(':');
                var prefix = colon < 0 ? "" : destination[..colon];
                // A hostname followed by a port can also look like a URI scheme.
                var hasHostPort = (prefix.Contains('.') || prefix.Equals("localhost", StringComparison.OrdinalIgnoreCase))
                    && colon + 1 < destination.Length && char.IsAsciiDigit(destination[colon + 1]);
                if (!Uri.CheckSchemeName(prefix) || hasHostPort)
                    destination = "http://" + destination;

                if (Uri.TryCreate(destination, UriKind.Absolute, out var uri)
                    && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                    && uri.IsWellFormedOriginalString())
                    return true;
            }

            ModelState.AddModelError(nameof(Link.URL), "Enter a valid HTTP or HTTPS destination URL.");
            return false;
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult CreateLink(Link link)
        {
            if (!TryGetDestination(link.URL, out var destination))
            {
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

            link.URL = destination;
            link.Creator = User.Identity.Name;
            link.ShortURL = string.IsNullOrWhiteSpace(link.ShortURL) ? GenerateShortURL() : link.ShortURL;

            db.InsertShortUrl(link);

            return RedirectToAction(nameof(LinkCreated), new { shortUrl = link.ShortURL });
        }

        public ActionResult LinkCreated(string shortUrl)
        {
            var link = string.IsNullOrWhiteSpace(shortUrl) ? null : db.GetShortUrl(shortUrl);
            return link == null ? View("InvalidShortURL") : View(link);
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
