# Link Shortener

CLC's link shortening and one-time note service.

## Deployment

1. Install the .NET 10 Hosting Bundle and create an HTTPS IIS site. Enable IIS Anonymous Authentication so Entra sign-in and public routes reach the application. Set the application pool identity to the domain service account used for SQL Server access, set its Start Mode to **AlwaysRunning**, and set **Preload Enabled** to **True**. [IIS Hosting Bundle](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/iis/hosting-bundle?view=aspnetcore-10.0)
2. In the deployed application's required `config/settings.json`, set `AzureAd:Instance` (typically `https://login.microsoftonline.com/`), `AzureAd:TenantId`, `AzureAd:ClientId`, and `AzureAd:CallbackPath` (typically `/signin-oidc`). Register the public HTTPS callback URL as a **Web** redirect URI in Microsoft Entra. Supply the client credential through deployment configuration, for example with `AzureAd__ClientSecret`. [Microsoft Entra sign-in configuration](https://learn.microsoft.com/en-us/entra/msidweb/getting-started/quickstart-webapp)
3. Set `Clc:DatabaseServer` to the SQL Server host or instance serving the existing `LinkShortener` and `clc_web_membership` databases. The application falls back to `Clc:Database:Hostname` when `Clc:DatabaseServer` is unset or blank. If you migrate the setting, update environment-specific overrides as well. Set `Clc:ApplicationName` to the application's name in the CLC membership database.
4. Grant the IIS application pool identity SQL access using integrated authentication. It needs `SELECT` on `clc_web_membership.dbo.UsersInRoles` and `clc_web_membership.dbo.EmailDomains`, `SELECT`, `INSERT`, and `UPDATE` on `LinkShortener.dbo.Links`, and `INSERT` on `LinkShortener.dbo.LinkVisits`. Follow the [one-time note deployment and operations guide](sql/README.md) for note-table permissions, schema verification, SQL Server Agent cleanup, and trusted proxy configuration. Publish the application to the IIS site.

## Link administration

`Clc.Authentication` signs users in through Microsoft Entra and looks up their CLC organization by username domain. The application checks the `Admin` role claim to allow listing and editing all short links. Manage that role in `clc_web_membership.dbo.UsersInRoles` with `RoleName` set to `Admin`, the signed-in `Username`, and the configured `ApplicationName`.

## PR validation

GitHub Actions restores and builds the application and test project, runs the Release tests, and verifies a Release publish. SQL integration tests are reported as skipped when `LINK_SHORTENER_TEST_SQL` is unset. Run them separately against a disposable SQL Server as described in the [local verification instructions](sql/README.md#local-verification).
