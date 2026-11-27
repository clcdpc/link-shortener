using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Clc.LinkShortener.Models
{
    public class Link
    {
        public string ShortURL { get; set; }
        public string Creator { get; set; }
        public DateTime CreationDate { get; set; }
        public string URL { get; set; }
        public DateTime? ModifiedDate { get; set; }
    }
}
