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

By default, link visits, note audits, and note rate limits use the connection peer address. To distinguish visitors behind a Cloudflare Tunnel, enable forwarding in `C:\ProgramData\clc-link-shortener\Config\application.json` and list the connector addresses that ASP.NET Core sees at IIS. Replace the example IPv4 and IPv6 addresses with your connectors' addresses. [Clc.Cloudflare.AspNetCore](https://www.nuget.org/packages/Clc.Cloudflare.AspNetCore/)

```json
{
  "Cloudflare": {
    "Enabled": true,
    "TrustedNetworks": ["192.0.2.10/32", "2001:db8::10/128"]
  }
}
```

When migrating an existing forwarded setup, carry its effective trusted ranges into `Cloudflare:TrustedNetworks` (formerly `ClientIp:TrustedProxyNetworks`), verify them against the immediate peers, and keep `Cloudflare:Enabled` set to `true`. Leave forwarding disabled where it was disabled before cutover. The package accepts a single address in `CF-Connecting-IP` from a trusted peer. If you used `ClientIp:ForwardedHeaderName`, configure the intermediary to verify its upstream and overwrite `CF-Connecting-IP` with the visitor address. Restart the application after changing these settings. With forwarding disabled, visitors behind one connector share its rate limit.

Confirm the existing cleanup job and run the database preflight before cutover. The IIS application identity needs SELECT, INSERT, and DELETE on dbo.Notes and INSERT on both note audit tables. The artifact contains the supplied 5,521 words as an embedded resource and validates them at startup. Follow the manual cutover and rollback procedure for the existing IIS site. [IIS cutover](../README.md#manual-cutover)

Consumption uses fixed windows of 10 attempts per minute and 100 per hour per client IP, plus 300 per minute across the application instance. The shared confirmation-page limit is 30 GETs per minute per client IP, including legacy display GETs. Rejected requests return HTTP 429 without reaching the database. These in-memory windows are per instance and reset on recycle. The documented deployment uses one Windows host with a persistent, user-scoped DPAPI key ring.

Note responses send `Cache-Control: no-store`, `Referrer-Policy: no-referrer`, and `X-Robots-Tag: noindex, nofollow`. Keep CDN cache overrides off the note routes. IIS, Cloudflare, and other infrastructure can still record request paths or query strings. Review those settings and access controls for `/note` traffic to avoid capturing share links. Keep verbose HTTP, MVC, and SQL tracing off for note requests and review any diagnostics that include request bodies or SQL parameter values.

## 4. Verify after deployment

1. Open the public HTTPS site, confirm Entra sign-in returns to the same site, and check that an authorized account can list its links. Follow a known short link and confirm both its destination and the visitor address in `dbo.LinkVisits`. Create a disposable note. Check that its share URL contains four hyphen-separated lowercase words.
2. Open a new note URL and an available legacy five-character URL. Each GET should show the warning. Submit each form, then check that a second submission shows the unavailable response.
3. Check that expired and missing notes produce the same unavailable response. In a disposable database, confirm expiration with SQL Server Agent stopped, then run the cleanup and verify it removes expired rows while retaining fresh rows.
4. Check `dbo.NoteViews.ViewerIP` and SQL Server local view time for the disposable note. The request-header audit can contain a recognized `Sec-Fetch-Site` category. When forwarding is enabled, confirm both visit and note audit rows contain the visitor's IP through the trusted connector. From a dedicated test client, issue 31 confirmation GETs in one minute. Expect HTTP 429 by request 31 and a working short-link redirect afterward.
5. Inspect `Logs\clc-link-shortener-yyyy-MM-dd.log` under the application data root. Look for a startup entry with local time and offset. Clc.Logging writes plaintext, rolls daily or at 50 MiB and defaults to 30 days and 300 archives. File failure falls back to stderr. Set levels and optional file limits in the external `Logging` section. The application suppresses ASP.NET Core diagnostics below Warning to protect note URLs and bound arguments.
6. While signed in, open a fresh note confirmation form. Check for an encrypted key file in `Keys\`, recycle the existing application pool, and submit the still-open form. Confirm it succeeds once, sign-in survives, and a new startup entry appears. Reuse the same pool account and profile on subsequent deployments. Limit backups of the key ring and profile to approved operators.

## Local verification

```powershell
dotnet test tests/LinkShortener.Tests.csproj
$env:LINK_SHORTENER_TEST_SQL = 'Server=localhost;Integrated Security=true;TrustServerCertificate=true'
dotnet test tests/LinkShortener.Tests.csproj
```

The tests reference the application and its existing build dependencies. SQL tests are skipped unless `LINK_SHORTENER_TEST_SQL` is set. Use a disposable SQL Server instance and a login allowed to create and drop databases. Tests create a randomly named database with the development note schema, seed synthetic data, and clean up that database. They run without Agent. To exercise the Agent installation twice and verify the hourly schedule, use an isolated Agent-capable instance with a test database named `LinkShortener` initialized from [the test fixture](../tests/fixtures.sql). Seed a fresh and an expired row, run the job, then inspect the remaining rows and job history.
