using Clc.LinkShortener.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Clc.LinkShortener.Controllers
{
    [Authorize]
    public class NoteController(LinksDbHelper db, NoteWords words, ClientIp clientIp) : Controller
    {
        [HttpGet]
        public ActionResult Index() => View();

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Create(string noteText)
        {
            if (string.IsNullOrWhiteSpace(noteText))
            {
                ModelState.AddModelError(string.Empty, "Enter the text for your one-time note.");
                return View("Index");
            }

            var id = db.AddNote(User.Identity.Name, noteText, words.Generate);
            return View((object)id);
        }

        [AllowAnonymous]
        [HttpPost]
        [ValidateAntiForgeryToken]
        [NoteRequestLimit(NoteRequestType.Consumption)]
        public ActionResult Display([FromForm] string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length > LinksDbHelper.NoteIdCapacity)
            {
                return View("InvalidNote");
            }

            var noteText = db.ConsumeNote(id, clientIp.Resolve(HttpContext), NoteRequests.GetFetchSite(Request));
            return noteText == null ? View("InvalidNote") : View((object)noteText);
        }

        [AllowAnonymous]
        [HttpGet]
        [ActionName(nameof(Display))]
        [NoteRequestLimit(NoteRequestType.Warning)]
        public ActionResult ConfirmDisplay(string id) => View("Warning", (object)id);

        [AllowAnonymous]
        [HttpGet]
        [NoteRequestLimit(NoteRequestType.Warning)]
        public ActionResult Warning(string noteId) => View((object)noteId);
    }
}
