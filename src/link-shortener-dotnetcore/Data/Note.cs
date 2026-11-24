using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Clc.LinkShortener.DotNetCore.Data
{
    public class Note
    {
        public string ID { get; set; }
        public DateTime CreationDate { get; set; }
        public string Creator { get; set; }
        public string NoteText { get; set; }
    }
}
