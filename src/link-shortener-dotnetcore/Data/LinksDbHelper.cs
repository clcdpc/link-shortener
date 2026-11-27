using Dapper;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Clc.LinkShortener.DotNetCore.Data
{
    public class LinksDbHelper
    {
        public string Server { get; set; }
        public string ConnectionString { get; }

        public LinksDbHelper(string databaseHostname)
        {
            ConnectionString = $"Server={databaseHostname};Database=LinkShortener;Trusted_Connection=True;TrustServerCertificate=True;";
        }

        public Link GetShortUrl(string shortUrl)
        {
            using (var sql = new SqlConnection(ConnectionString))
            {
                var sqlCommand = "select * from Links where ShortURL = @shortUrl";

                return sql.QueryFirstOrDefault<Link>(sqlCommand, new { shortUrl });
            }
        }

        public bool InsertShortUrl(string shortUrl, string url, string creator)
        {
            using (var sql = new SqlConnection(ConnectionString))
            {
                var sqlCommand = "insert into Links values (@shortUrl, @creator, getdate(), @url, null)";

                return sql.Execute(sqlCommand, new { shortUrl, url, creator }) > 0;
            }
        }

        public bool InsertShortUrl(Link link)
        {
            return InsertShortUrl(link.ShortURL, link.URL, link.Creator);
        }

        public IEnumerable<Link> GetLinks()
        {
            using (var sql = new SqlConnection(ConnectionString))
            {
                var sqlCommand = "select * from links";

                return sql.Query<Link>(sqlCommand);
            }
        }

        public IEnumerable<Link> GetLinksForUser(string username)
        {
            using (var sql = new SqlConnection(ConnectionString))
            {
                var sqlCommand = "select * from links where creator = @username";

                return sql.Query<Link>(sqlCommand, new { username });
            }
        }

        public bool UpdateLink(string shortUrl, string url)
        {
            using (var sql = new SqlConnection(ConnectionString))
            {
                var sqlCommand = "update links set url = @url, ModifiedDate = getdate() where shortUrl = @shortUrl";

                return sql.Execute(sqlCommand, new { shortUrl, url }) > 0;
            }
        }

        public bool AddLinkVisit(string shortUrl, string ipAddress, string referrer)
        {
            using (var sql = new SqlConnection(ConnectionString))
            {
                var sqlCommand = "insert into LinkVisits (ShortURL, VisitorIP, VisitTime, Referrer) values(@shortUrl, @ipAddress, getdate(), @referrer)";

                return sql.Execute(sqlCommand, new { shortUrl, ipAddress, referrer }) > 0;
            }
        }

        public bool AddNote(Note note)
        {
            using (var sql = new SqlConnection(ConnectionString))
            {
                var sqlCommand = "insert into notes values (@id, @creator, getdate(), @noteText)";

                return sql.Execute(sqlCommand, new { note.ID, note.NoteText, note.Creator }) > 0;
            }
        }

        public Note GetNote(string noteId) => SelectFirst<Note>("select * from notes where id = @noteId", new { noteId });

        T SelectFirst<T>(string sqlCommand, object parameters)
        {
            using (var sql = new SqlConnection(ConnectionString))
            {
                return sql.QueryFirstOrDefault<T>(sqlCommand, parameters);
            }
        }

        IEnumerable<T> Select<T>(string sqlCommand, object parameters)
        {
            using (var sql = new SqlConnection(ConnectionString))
            {
                return sql.Query<T>(sqlCommand, parameters);
            }
        }

        public bool AddNoteView(string noteId, string ipAddress)
        {
            using var sql = new SqlConnection(ConnectionString);
            var sqlCommand = "insert into NoteViews (NoteID, ViewerIP, ViewTime) values(@noteId, @ipAddress, getdate())";

            return sql.Execute(sqlCommand, new { noteId, ipAddress }) > 0;
        }

        public bool WriteHeaders(string noteId, HttpContext context)
        {
            var skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Authorization", "Cookie", "Set-Cookie" };

            var rows = context.Request.Headers
                .Where(h => !skip.Contains(h.Key))
                .SelectMany(h => h.Value, (h, v) => new
                {
                    NoteId = noteId,
                    Name = h.Key,
                    Value = v
                })
                .ToList();

            if (rows.Count == 0) return true;

            using var conn = new SqlConnection(ConnectionString);

            var cols = new[] { "NoteId", "Name", "Value" };

            var valuesSql = string.Join(", ",
                Enumerable.Range(0, rows.Count)
                    .Select(i => $"(@{cols[0]}{i}, @{cols[1]}{i}, @{cols[2]}{i})"));

            var sql = $"insert into dbo.NoteViewRequestHeaders ({string.Join(", ", cols)}) values {valuesSql};";

            var dp = new DynamicParameters();
            for (int i = 0; i < rows.Count; i++)
            {
                dp.Add($"{cols[0]}{i}", rows[i].NoteId);
                dp.Add($"{cols[1]}{i}", rows[i].Name);
                dp.Add($"{cols[2]}{i}", rows[i].Value);
            }

            conn.Execute(new CommandDefinition(sql, dp));

            return true;
        }

        public bool RemoveNote(string noteId)
        {
            using (var sql = new SqlConnection(ConnectionString))
            {
                var sqlCommand = "delete from notes where id = @noteId";

                return sql.Execute(sqlCommand, new { noteId }) > 0;
            }
        }
    }
}
