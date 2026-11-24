using Clc.LinkShortener.DotNetCore.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Clc.LinkShortener.DotNetCore.Controllers
{
    [Authorize]
    public class NoteController : Controller
    {
        LinksDbHelper db;

        public NoteController(LinksDbHelper _db)
        {
            db = _db;
        }

        public ActionResult Index()
        {
            return View();
        }

        public ActionResult Create(string noteText)
        {
            var note = new Note { ID = GenerateShortURL(), CreationDate = DateTime.Now, Creator = User.Identity.Name, NoteText = noteText };
            db.AddNote(note);

            return View(note);
        }

        [AllowAnonymous]
        public ActionResult Display(string id)
        {
            var note = db.GetNote(id);
            if (note == null) return View("InvalidNote");

            db.AddNoteView(id, Request.GetTrueClientIP());
            db.WriteHeaders(id, ControllerContext.HttpContext);
            db.RemoveNote(id);            

            return View((object)note.NoteText);
        }

        [AllowAnonymous]
        public ActionResult Warning(string noteId)
        {
            return View((object)noteId);
        }

        private string GenerateShortURL()
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


            if (db.GetNote(shortURL) != null)
            {
                shortURL = GenerateShortURL();
            }

            return shortURL;
        }
    }
}
