using Clc.LinkShortener.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Web;
using System.Web.Mvc;

namespace Clc.LinkShortener.Controllers
{
    [Authorize]
    public class NoteController : Controller
    {
        LinkEntities db;

        public NoteController()
        {
            db = new LinkEntities();
        }

        public ActionResult Index()
        {
            return View();
        }

        public ActionResult Create(string noteText)
        {
            var note = new Note { ID = GenerateShortURL(), CreationDate = DateTime.Now, Creator = User.Identity.Name, NoteText = noteText };
            db.Notes.Add(note);
            db.SaveChanges();

            return View(note);
        }

        [AllowAnonymous]
        public ActionResult Display(string noteId)
        {
            var note = db.Notes.SingleOrDefault(n => n.ID == noteId);
            if (note == null) return View("InvalidNote");

            var view = new NoteView
            {
                ViewerIP = LinkUtils.VisitorIP,
                ViewTime = DateTime.Now,
                NoteID = noteId
            };
            db.NoteViews.Add(view);
            db.Notes.Remove(note);
            db.SaveChanges();
            
            return View((object)note.NoteText);
        }

        public string GenerateShortURL()
        {
            var urlLength = 5;
            var rand = new Random();
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz1234567890";
            var linkBuilder = new StringBuilder();

            for (int i = 0; i < urlLength; i++)
            {
                linkBuilder.Append(chars[rand.Next(chars.Length)]);
            }

            var shortURL = linkBuilder.ToString();


            if (db.Notes.Any(n => n.ID == shortURL))
            {
                shortURL = GenerateShortURL();
            }

            return shortURL;
        }
    }
}
