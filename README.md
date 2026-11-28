# Link Shortener

CLC's link shortening and one-time note service.

## Windows hosting

Production stays at `C:\web\shorturl` until ThreatLocker approval and manual cutover. The .NET 10 application uses the following layout. Grant access to the existing IIS pool's domain service account on each directory, and check effective permissions including inherited grants.

| Directory under `C:\ProgramData\clc-link-shortener\` | Contents | Pool access |
| --- | --- | --- |
| `Application\` | Published binaries, compiled views and static files | Read and Execute |
| `Config\` | Authoritative `application.json`, including the Entra credential | Read |
| `Keys\` | Persistent Data Protection key ring | Modify |
| `Logs\` | Rotating application logs | Modify |

Only administrators or deployment operators should update binaries and configuration. Restrict config, logs and keys to the identities that need them. The layout follows Clc.Deployment conventions. This site is manually managed. Future toolkit adoption may require a separate procedure for the existing site and directories.

## Manual cutover

1. Obtain ThreatLocker approval for the .NET 10 Hosting Bundle and execution from the new `Application` path. Install the approved bundle. Record the old physical path, pool identity, profile settings and bindings. Reuse the existing site, certificate and pool. Keep IIS Anonymous Authentication enabled, the pool **AlwaysRunning**, and site preload enabled.
2. Provision the four directories above. For path discovery, run `dotnet Clc.LinkShortener.dll --clc-appdata-directory` from the staged Release publish. This prints the OS-resolved data root before loading configuration.
3. As the provisioning operator, copy the published `config\example.json` to `Config\application.json`. Replace all `REPLACE_ME` values. Reconcile the **effective** values from the old `config/settings.json`, environment-specific files, appsettings and IIS environment variables. For example, move `AzureAd__ClientSecret` to the nested `AzureAd.ClientSecret` property or retain the existing credential mechanism. Keep the old configuration available for rollback. The CLC bootstrap can create a missing file under an identity with write access and stops on creation or unresolved markers. Provisioning it ahead of time allows read-only runtime access.
4. Apply the per-directory permissions above. Preserve the existing Entra registration, tenant, client ID, domain, credential and registered HTTPS Web callback (normally `/signin-oidc`). Keep `Clc:ApplicationName` at `link-shortener` for membership lookups. Set `Clc:DatabaseServer` to the existing SQL instance. A blank or missing value falls back to `Clc:Database:Hostname`. Keep `AllowedHosts` and any required listener settings in the authoritative file. Changes take effect on restart.
5. Start with an empty new `Keys` directory and **Load User Profile** enabled for the same pool identity. Windows DPAPI protects keys for that user. The old host relied on framework-selected key storage and a content-root-based discriminator. This cutover intentionally starts a fresh ring with the fixed discriminator `clc-link-shortener`. Leave the old store and profile intact. Users may need to sign in again and reload forms issued before cutover.
6. Copy a clean Release publish into `Application`. The artifact should contain `config\example.json` and no operational settings. Confirm existing integrated SQL permissions: `SELECT` on membership tables `UsersInRoles` and `EmailDomains`, `SELECT/INSERT/UPDATE` on `LinkShortener.dbo.Links`, and `INSERT` on `dbo.LinkVisits`. Check note permissions, schema, the existing Agent job and the immediate Cloudflare peers. [SQL and proxy operations](sql/README.md)
7. Stop the existing IIS site and quiesce its application.
8. Change its physical path from `C:\web\shorturl` to `C:\ProgramData\clc-link-shortener\Application`.
9. Start the site. Complete the sign-in, redirect, note, logging and recycle checks. [Post-deployment verification](sql/README.md#4-verify-after-deployment)
10. Retain the original deployment, configuration, pool profile and key store until acceptance is complete.

**Rollback.** Stop the site, restore its physical path to `C:\web\shorturl` and restore the recorded pool settings and any legacy environment values. Restart and verify sign-in and an existing redirect. The old build uses its original configuration and key store. Cookies issued by the new ring require a fresh sign-in on the old build, and open forms may need a reload. Preserve the new configuration and ring for a retry. Both versions use the same databases. Rolling back binaries does not undo visits or restore consumed notes.

## Link administration

`Clc.Authentication` signs users in through Microsoft Entra and looks up their CLC organization by username domain. The application checks the `Admin` role claim to allow listing and editing all short links. Manage that role in `clc_web_membership.dbo.UsersInRoles` with `RoleName` set to `Admin`, the signed-in `Username`, and the configured `ApplicationName`.

## PR validation

GitHub Actions restores and builds the application and test project, runs the Release tests, and verifies a Release publish. SQL integration tests are reported as skipped when `LINK_SHORTENER_TEST_SQL` is unset. Run them separately against a disposable SQL Server as described in the [local verification instructions](sql/README.md#local-verification).

The test hosts create configuration, logs and keys in unique temporary directories. For an interactive development run, build Debug and place the package's local identity override beside `src/bin/Debug/net10.0/Clc.LinkShortener.dll` in an `appsettings.json` containing `{"ClcConfiguration":{"ApplicationDirectoryName":"clc-link-shortener-dev"}}`. Run `dotnet run --project src --no-build -- --clc-appdata-directory` to confirm the separate root, then provision development settings there. Keep this build-local override out of Release artifacts.
