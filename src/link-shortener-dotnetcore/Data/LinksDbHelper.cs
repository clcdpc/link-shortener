using Clc.Configuration;
using Dapper;
using System;
using System.Collections.Generic;
using Microsoft.Data.SqlClient;

namespace Clc.LinkShortener.DotNetCore.Data
{
    public class LinksDbHelper
    {
        public string ConnectionString { get; }

        public LinksDbHelper(AppSettings config)
            : this(new SqlConnectionStringBuilder
            {
                DataSource = config.Database.Hostname,
                InitialCatalog = "LinkShortener",
                IntegratedSecurity = true,
                TrustServerCertificate = true
            })
        {
        }

        internal LinksDbHelper(SqlConnectionStringBuilder connection)
        {
            ConnectionString = connection.ConnectionString;
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

        internal const int NoteIdCapacity = 255;
        private const int NoteInsertAttempts = 5;

        internal string AddNote(string creator, string noteText, Func<string> generateId)
        {
            using var sql = new SqlConnection(ConnectionString);
            for (var attempt = 0; attempt < NoteInsertAttempts; attempt++)
            {
                var id = generateId();
                try
                {
                    const string insert = """
                        INSERT INTO dbo.Notes (ID, Creator, CreationDate, NoteText)
                        VALUES (@id, @creator, GETDATE(), @noteText);
                        """;
                    if (sql.Execute(insert, new { id, creator, noteText }) != 1)
                    {
                        throw new InvalidOperationException("One-time note creation did not insert a row.");
                    }

                    return id;
                }
                catch (SqlException error) when (error.Number is 2601 or 2627)
                {
                    // The Notes primary key arbitrates collisions, including concurrent inserts.
                }
                catch (SqlException error)
                {
                    throw new InvalidOperationException($"One-time note creation failed (SQL Server error {error.Number}).");
                }
            }

            throw new InvalidOperationException($"One-time note creation failed after {NoteInsertAttempts} identifier collisions.");
        }

        internal string ConsumeNote(string noteId, string ipAddress, string fetchSite)
        {
            using var sql = new SqlConnection(ConnectionString);
            const string consume = """
                SET NOCOUNT ON;
                SET XACT_ABORT ON;

                DECLARE @consumed TABLE (ID varchar(255) NOT NULL, NoteText varchar(max) NOT NULL);

                BEGIN TRY
                    BEGIN TRANSACTION;

                    DELETE FROM dbo.Notes
                    OUTPUT DELETED.ID, DELETED.NoteText INTO @consumed (ID, NoteText)
                    WHERE ID = @noteId
                        AND CreationDate > DATEADD(DAY, -30, GETDATE());

                    IF EXISTS (SELECT 1 FROM @consumed)
                    BEGIN
                        INSERT INTO dbo.NoteViews (NoteID, ViewerIP, ViewTime)
                        SELECT ID, @ipAddress, GETDATE() FROM @consumed;

                        IF @@ROWCOUNT <> 1
                            THROW 51000, 'One-time note view audit did not insert a row.', 1;

                        IF @fetchSite IS NOT NULL
                        BEGIN
                            INSERT INTO dbo.NoteViewRequestHeaders (NoteId, Name, Value)
                            SELECT ID, N'Sec-Fetch-Site', @fetchSite FROM @consumed;

                            IF @@ROWCOUNT <> 1
                                THROW 51001, 'One-time note request audit did not insert a row.', 1;
                        END;
                    END;

                    COMMIT TRANSACTION;
                END TRY
                BEGIN CATCH
                    IF XACT_STATE() <> 0
                        ROLLBACK TRANSACTION;
                    THROW;
                END CATCH;

                SELECT NoteText FROM @consumed;
                """;

            try
            {
                return sql.QuerySingleOrDefault<string>(consume, new
                {
                    noteId = new DbString { Value = noteId, Length = NoteIdCapacity, IsAnsi = true },
                    ipAddress,
                    fetchSite
                });
            }
            catch (SqlException error)
            {
                // SQL Server error messages can include statement values. Keep them out of application logs.
                throw new InvalidOperationException($"One-time note consumption failed (SQL Server error {error.Number}).");
            }
        }
    }
}
