# One-time note deployment and operations

## 1. Verify the target database

Run the schema preflight against the target `LinkShortener` database before making any changes. The operator needs database-level `VIEW DEFINITION` so that dependencies are visible.

```powershell
sqlcmd -S <instance> -d LinkShortener -E -b -i sql/verify-note-schema.sql
```

The development catalog showed `dbo.Notes.ID varchar(255)` with an enabled primary key, `dbo.NoteViews.NoteID varchar(255)`, and `dbo.NoteViewRequestHeaders.NoteId nvarchar(64)`. All support the maximum generated length of 39 characters. No identifier or index migration is required for this schema. The preflight makes no changes and stops on incompatible types, insufficient capacity, missing uniqueness, or enabled foreign keys, triggers, or check constraints involving the note tables. If it fails, have the DBA review the target schema and its dependencies before proceeding.

## 2. Install the hourly cleanup job

Use a SQL Server sysadmin account on the same instance. SQL Server Agent must be available and running. The installer creates or updates the local job `LinkShortener - Expired Note Cleanup` and its dedicated hourly schedule. New jobs default to the installing login as owner. Use a stable job owner with permission to delete from `LinkShortener.dbo.Notes`.

```powershell
sqlcmd -S <instance> -E -b -i sql/install-expired-note-cleanup.sql
sqlcmd -S <instance> -E -b -i sql/verify-expired-note-cleanup.sql
```

To run the job on demand, use **Start Job at Step** in SSMS or execute the following with an account permitted to start the job.

```sql
EXEC msdb.dbo.sp_start_job @job_name = N'LinkShortener - Expired Note Cleanup';
EXEC msdb.dbo.sp_help_jobhistory @job_name = N'LinkShortener - Expired Note Cleanup';
```

Check the most recent job history after execution completes. Each run permanently deletes rows with `CreationDate <= DATEADD(DAY, -30, GETDATE())` using SQL Server local time. The application applies the same cutoff at consumption even if the job is disabled or unavailable.

## 3. Configure the proxy and deploy the application

The application uses the connection peer address by default. When that peer is inside a configured trusted range, it can use a single address from `CF-Connecting-IP`. Add the current Cloudflare IPv4 and IPv6 ranges to `ClientIp:TrustedProxyNetworks` in the existing `config/settings.json` and keep them current. This example uses documentation addresses that must be replaced. [Cloudflare IP ranges](https://www.cloudflare.com/ips/) · [Cloudflare visitor IP headers](https://developers.cloudflare.com/fundamentals/reference/http-headers/)

```json
{
  "ClientIp": {
    "TrustedProxyNetworks": ["192.0.2.0/24", "2001:db8::/32"]
  }
}
```

For an intermediary between Cloudflare and IIS, configure that intermediary to verify upstream Cloudflare peers and overwrite the client-address header on every request. Trust only the addresses of those intermediaries as seen by the ASP.NET Core app. If the intermediary writes a different single-address header, set `ClientIp:ForwardedHeaderName` to its name. Check what IIS exposes as the peer before finalizing the ranges, particularly with out-of-process IIS hosting or additional forwarding middleware. An unconfigured proxy is counted as one peer and shares its rate limit across visitors.

Deploy the .NET 10 application after the database preflight and job installation. The IIS application identity needs SELECT, INSERT, and DELETE on dbo.Notes and INSERT on both note audit tables. The artifact contains the supplied 5,521 words as an embedded resource and validates them at startup. No dictionary file is needed on IIS.

Consumption uses fixed windows of 10 attempts per minute and 100 per hour per client IP, plus 300 per minute across the application instance. The shared confirmation-page limit is 30 GETs per minute per client IP, including legacy display GETs. Rejected requests return HTTP 429 without reaching the database. These in-memory windows are per instance and reset on recycle. A deployment with multiple instances has an independent application-wide counter on each instance. Share ASP.NET Core Data Protection keys across instances so that any instance can validate the antiforgery token issued on the warning page. [ASP.NET Core web farm configuration](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/web-farm?view=aspnetcore-10.0)

Note responses send `Cache-Control: no-store`, `Referrer-Policy: no-referrer`, and `X-Robots-Tag: noindex, nofollow`. Keep CDN cache overrides off the note routes. IIS, Cloudflare, and other infrastructure can still record request paths or query strings. Review those settings and access controls for `/note` traffic to avoid capturing share links. Keep verbose HTTP, MVC, and SQL tracing off for note requests and review any diagnostics that include request bodies or SQL parameter values.

## 4. Verify after deployment

1. Confirm the app starts and create a note. Check that its share URL contains four hyphen-separated lowercase words.
2. Open a new note URL and an available legacy five-character URL. Each GET should show the warning. Submit each form, then check that a second submission shows the unavailable response.
3. Check that expired and missing notes produce the same unavailable response. In a disposable database, confirm expiration with SQL Server Agent stopped, then run the cleanup and verify it removes expired rows while retaining fresh rows.
4. Check that the note-view audit records the client address and SQL Server local view time. The request-header audit can contain a recognized `Sec-Fetch-Site` category. Verify the trusted address behind the proxy and repeat enough requests to see HTTP 429 without affecting short-link redirects.

## Local verification

```powershell
dotnet test tests/LinkShortener.Tests/LinkShortener.Tests.csproj
$env:LINK_SHORTENER_TEST_SQL = 'Server=localhost;Integrated Security=true;TrustServerCertificate=true'
dotnet test tests/LinkShortener.Tests/LinkShortener.Tests.csproj
```

The tests reference the application and its existing build dependencies. SQL tests are skipped unless `LINK_SHORTENER_TEST_SQL` is set. Use a disposable SQL Server instance and a login allowed to create and drop databases. Tests create a randomly named database with the development note schema, seed synthetic data, and clean up that database. They run without Agent. To exercise the Agent installation twice and verify the hourly schedule, use an isolated Agent-capable instance with a test database named `LinkShortener` initialized from [the test fixture](../tests/LinkShortener.Tests/fixtures.sql). Seed a fresh and an expired row, run the job, then inspect the remaining rows and job history.
