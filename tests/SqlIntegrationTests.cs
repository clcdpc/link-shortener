using System.Net;

namespace LinkShortener.Tests;

public class SqlIntegrationTests(SqlDatabase database) : IClassFixture<SqlDatabase>
{
    [SqlFact]
    public async Task PrimaryKeyCollisionsRegenerateAndHaveABoundedRetry()
    {
        const string collision = "same-same-same-same";
        const string other = "word-word-word-word";
        await database.SeedNoteAsync(collision, "first note");
        var picks = 0;
        var helper = database.Helper;

        Assert.Equal(other, helper.AddNote("test.user", "second note", () => ++picks == 1 ? collision : other));
        Assert.Equal(2, picks);
        Assert.Equal("first note", await database.ReadAsync<string>(
            "SELECT NoteText FROM dbo.Notes WHERE ID = @collision;", new { collision }));

        var error = Assert.Throws<InvalidOperationException>(() => helper.AddNote("test.user", "third note", () =>
        {
            picks++;
            return collision;
        }));
        Assert.Equal(7, picks);
        Assert.Contains("identifier collisions", error.Message);
        Assert.DoesNotContain(collision, error.ToString());
        Assert.DoesNotContain("third note", error.ToString());
    }

    [SqlFact]
    public async Task OldIdentifiersAndPreexistingWordsWorkAndAllUnavailableStatesMatch()
    {
        await database.SeedNoteAsync("Old12", "legacy text", daysOld: 29);
        await database.SeedNoteAsync("zzzzzzzzz-zzzzzzzzz-zzzzzzzzz-zzzzzzzzz", "old dictionary text");
        await database.SeedNoteAsync("Edge1", "cutoff text", daysOld: 30);
        await database.SeedNoteAsync("Older", "expired text", daysOld: 31);

        using var site = new TestSite(database.Helper);
        var token = await site.AntiforgeryAsync("/note/Old12");
        using var legacy = await site.PostAsync("/Note/Display", token, ("id", "Old12"));
        NoteHttpTests.AssertPrivacy(legacy);
        Assert.Contains("legacy text", await legacy.Content.ReadAsStringAsync());

        using var priorDictionary = await site.PostAsync("/Note/Display", token,
            ("id", "zzzzzzzzz-zzzzzzzzz-zzzzzzzzz-zzzzzzzzz"));
        Assert.Contains("old dictionary text", await priorDictionary.Content.ReadAsStringAsync());

        using var missing = await site.PostAsync("/Note/Display", token, ("id", "None1"));
        Assert.Equal(HttpStatusCode.OK, missing.StatusCode);
        NoteHttpTests.AssertPrivacy(missing);
        var unavailable = await missing.Content.ReadAsStringAsync();
        Assert.Contains("doesn't exist", unavailable, StringComparison.OrdinalIgnoreCase);
        foreach (var id in new[] { "Old12", "Edge1", "Older" })
        {
            using var response = await site.PostAsync("/Note/Display", token, ("id", id));
            Assert.Equal(missing.StatusCode, response.StatusCode);
            Assert.Equal(unavailable, await response.Content.ReadAsStringAsync());
            NoteHttpTests.AssertPrivacy(response);
        }

        Assert.Equal(2, await database.ReadAsync<int>("SELECT COUNT(*) FROM dbo.Notes WHERE ID IN ('Edge1', 'Older');"));
        Assert.Equal(0, await database.ReadAsync<int>("SELECT COUNT(*) FROM dbo.NoteViews WHERE NoteID IN ('Edge1', 'Older');"));
    }

    [SqlFact]
    public async Task ConcurrentRequestsReturnOneNoteAndAuditOnlyApprovedMetadata()
    {
        const string id = "race-race-race-race";
        const string noteText = "one concurrent reader";
        await database.SeedNoteAsync(id, noteText);

        using var site = new TestSite(database.Helper);
        var token = await site.AntiforgeryAsync($"/note/{id}");
        site.Client.DefaultRequestHeaders.Add("CF-Connecting-IP", "198.51.100.10");
        site.Client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "same-origin");
        site.Client.DefaultRequestHeaders.Add("Referer", $"https://example.test/note/{id}?token=synthetic");
        site.Client.DefaultRequestHeaders.Add("X-Extra-Diagnostic", "synthetic-secret");
        var bodies = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
        {
            using var response = await site.PostAsync("/Note/Display?private=synthetic", token, ("id", id));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await response.Content.ReadAsStringAsync();
        }));

        Assert.Single(bodies, body => body.Contains(noteText));
        Assert.Single(bodies, body => body.Contains("doesn't exist", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("203.0.113.10", await database.ReadAsync<string>(
            "SELECT ViewerIP FROM dbo.NoteViews WHERE NoteID = @id AND ViewTime <= GETDATE();", new { id }));
        Assert.Equal("Sec-Fetch-Site=same-origin", await database.ReadAsync<string>(
            "SELECT Name + '=' + Value FROM dbo.NoteViewRequestHeaders WHERE NoteId = @id;", new { id }));
    }

    [SqlFact]
    public async Task AuditFailureRollsBackTheDeleteAndEveryAuditRow()
    {
        const string id = "Hold1";
        const string noteText = "text kept during audit failure";
        await database.SeedNoteAsync(id, noteText);
        await database.ExecuteAsync("""
            ALTER TABLE dbo.NoteViewRequestHeaders ADD CONSTRAINT CK_Test_RejectNoteAudit CHECK (NoteId <> 'Hold1');
            """);
        try
        {
            using var site = new TestSite(database.Helper);
            var token = await site.AntiforgeryAsync($"/note/{id}");
            site.Client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "same-origin");
            using var response = await site.PostAsync("/Note/Display", token, ("id", id));
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            NoteHttpTests.AssertPrivacy(response);
            Assert.DoesNotContain(noteText, await response.Content.ReadAsStringAsync());

            Assert.Equal(noteText, await database.ReadAsync<string>(
                "SELECT NoteText FROM dbo.Notes WHERE ID = @id;", new { id }));
            Assert.Equal(0, await database.ReadAsync<int>(
                "SELECT COUNT(*) FROM dbo.NoteViews WHERE NoteID = @id;", new { id }));
            Assert.Equal(0, await database.ReadAsync<int>(
                "SELECT COUNT(*) FROM dbo.NoteViewRequestHeaders WHERE NoteId = @id;", new { id }));
        }
        finally
        {
            await database.ExecuteAsync("ALTER TABLE dbo.NoteViewRequestHeaders DROP CONSTRAINT CK_Test_RejectNoteAudit;");
        }
    }

    [SqlFact]
    public async Task NoteCreationFormStoresAWordIdentifierAndReturnsOnlyItsLink()
    {
        using var site = new TestSite(database.Helper);
        site.Client.DefaultRequestHeaders.Add("X-Test-User", "note.author");
        var token = await site.AntiforgeryAsync("/Note/Index");
        const string noteText = "only the person who confirms should see the text";
        using var response = await site.PostAsync("/Note/Create", token, ("noteText", noteText));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        NoteHttpTests.AssertPrivacy(response);
        var id = await database.ReadAsync<string>("SELECT ID FROM dbo.Notes WHERE Creator = 'note.author';");
        Assert.Matches("^[a-z]{1,9}(-[a-z]{1,9}){3}$", id);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains($"/note/{id}", html);
        Assert.DoesNotContain(noteText, html);
    }

    [SqlFact]
    public async Task LinkEditRequiresOwnershipOrAdminAndRedirectsAreExemptFromNoteLimits()
    {
        await database.ExecuteAsync("""
            INSERT INTO dbo.Links VALUES ('demo-link', 'test.owner', GETDATE(), 'https://example.test/old', NULL);
            """);

        using var site = new TestSite(database.Helper);
        site.Client.DefaultRequestHeaders.Add("X-Test-User", "another.user");
        var token = await site.AntiforgeryAsync("/Link/Index");
        const string edit = "/Link/Edit?shortUrl=demo-link";
        using var deniedGet = await site.Client.GetAsync(edit);
        Assert.Equal(HttpStatusCode.Forbidden, deniedGet.StatusCode);
        using var deniedPost = await site.PostAsync("/Link/Edit", token,
            ("shortUrl", "demo-link"), ("url", "https://example.test/denied"));
        Assert.Equal(HttpStatusCode.Forbidden, deniedPost.StatusCode);
        Assert.Equal("https://example.test/old", await database.ReadAsync<string>(
            "SELECT URL FROM dbo.Links WHERE ShortURL = 'demo-link';"));

        site.Client.DefaultRequestHeaders.Add("X-Test-Admin", "true");
        using var adminGet = await site.Client.GetAsync(edit);
        Assert.Equal(HttpStatusCode.OK, adminGet.StatusCode);
        var adminToken = await site.AntiforgeryAsync(edit);
        using var adminPost = await site.PostAsync("/Link/Edit", adminToken,
            ("shortUrl", "demo-link"), ("url", "https://example.test/updated"));
        Assert.Equal(HttpStatusCode.Redirect, adminPost.StatusCode);

        site.Client.DefaultRequestHeaders.Remove("X-Test-Admin");
        site.Client.DefaultRequestHeaders.Remove("X-Test-User");
        site.Client.DefaultRequestHeaders.Add("X-Test-User", "test.owner");
        using var ownerGet = await site.Client.GetAsync(edit);
        Assert.Equal(HttpStatusCode.OK, ownerGet.StatusCode);
        var ownerToken = await site.AntiforgeryAsync(edit);
        using var ownerPost = await site.PostAsync("/Link/Edit", ownerToken,
            ("shortUrl", "demo-link"), ("url", "https://example.test/owner"));
        Assert.Equal(HttpStatusCode.Redirect, ownerPost.StatusCode);

        site.Client.DefaultRequestHeaders.Remove("X-Test-User");
        for (var attempt = 0; attempt < 10; attempt++)
        {
            using var rejected = await site.Client.PostAsync("/Note/Display", null);
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        }

        using var limited = await site.Client.PostAsync("/Note/Display", null);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        using var followed = await site.Client.GetAsync("/demo-link");
        Assert.Equal(HttpStatusCode.Redirect, followed.StatusCode);
        Assert.Equal("https://example.test/owner", followed.Headers.Location?.OriginalString);
        Assert.False(followed.Headers.Contains("Referrer-Policy"));
    }
}
