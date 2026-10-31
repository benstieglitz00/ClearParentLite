# ClearParent Lite

ASP.NET Core MVC / Razor, .NET 10, ASP.NET Core Identity, EF Core SQL Server, locally hosted Quill 2, and PDFsharp/MigraDoc. One private timeline per parent, with explicit read-only grants. This repository is independent of the original ClearParent application.

## Run locally (Windows)

Install .NET 10 SDK and SQL Server Express LocalDB. From this repository:

```powershell
dotnet restore
dotnet tool restore
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet run --project ClearParentLite -- --initialize-db
dotnet dev-certs https --trust
dotnet run --project ClearParentLite --launch-profile https
```

Open **https://localhost:7273**. Use HTTPS: authentication, antiforgery and link-access cookies are Secure. The HTTP profile alone cannot support account workflows. `App:PublicOrigin` must match your HTTPS address. The initializer applies migrations and seeds Administrator, Parent and Read-only Guest roles; it does not create an administrator or a default password. The default database is the isolated `ClearParentLiteDev` database on `(localdb)\MSSQLLocalDB`.

Alternatively apply migrations explicitly:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet ef database update --project ClearParentLite
```

Register an account. In Development, with no SMTP configuration, confirmation/reset/invitation emails are written as `.eml` files to `ClearParentLite/App_Data/mail/`. Open the newest email locally and follow its link. Tokens are never included in browser account responses. This directory is private and ignored by Git. The in-process test suite captures emails through a test provider instead.

## External configuration

Use .NET user secrets for local credentials and environment/secret-store configuration in production. Do not commit credentials or production connection strings.

```powershell
dotnet user-secrets set 'Authentication:Google:ClientId' 'YOUR_CLIENT_ID' --project ClearParentLite
dotnet user-secrets set 'Authentication:Google:ClientSecret' 'YOUR_CLIENT_SECRET' --project ClearParentLite
dotnet user-secrets set 'Email:Host' 'smtp.example.com' --project ClearParentLite
dotnet user-secrets set 'Email:Port' '587' --project ClearParentLite
dotnet user-secrets set 'Email:From' 'no-reply@example.com' --project ClearParentLite
dotnet user-secrets set 'Email:Username' 'YOUR_SMTP_USERNAME' --project ClearParentLite
dotnet user-secrets set 'Email:Password' 'YOUR_SMTP_PASSWORD' --project ClearParentLite
```

Register Google's redirect URI as `https://localhost:7273/signin-google` (or the production HTTPS origin plus `/signin-google`). Without both Google secrets, the login page explains that email login is available. Google accounts require ClearParent's own email confirmation. Existing local accounts are never automatically linked by an email supplied by Google. Use the original sign-in method for such accounts; account-link management is not part of this MVP.

SMTP uses STARTTLS and authenticated credentials when supplied. Production has no pickup fallback. Invitation delivery failure revokes the newly created grant so it can be retried. Registration confirmation can be resent at `/account/resend-confirmation`. SMTP failure during registration leaves a recoverable unconfirmed account; configure SMTP and resend confirmation. No mail outbox/retry worker is included.

Environment variables use `__` in place of `:`, for example `ConnectionStrings__DefaultConnection`, `App__PublicOrigin`, `Email__Password`, and `Authentication__Google__ClientSecret`.

## Features and behavior

- Public introduction, resource guides and privacy information.
- Public registration, confirmed-email login, logout, password reset, lockout, Google OAuth when configured, profile settings and administrator enable/disable controls.
- Event-date ordered, paginated private timeline. Keyword search covers titles, descriptions and updates; date filters are inclusive. Stable ID tie-breaking.
- Keyboard-friendly new-entry modal (`N`) and dedicated form, rich-text formatting, pasted screenshots, drag/drop and file uploads with progress. Bullet and numbered lists retain their meaning after reloading. New-entry files upload after the entry is first saved. If a file fails, the entry remains saved and the draft/error stays in the browser so the remaining uploads can be retried. Stable browser draft/update/upload identifiers prevent duplicate records even if a successful server response is lost. Identifier reuse with different content is rejected. Unsaved drafts prompt before closing/leaving; session-expiry redirects preserve the draft for signing in in another tab and retrying.
- PNG/JPEG/GIF/WebP/PDF/DOC/DOCX originals in private local storage, behind authorized downloads. No SVG/HTML uploads, public URLs or remote tracking images.
- Immutable initial/edit snapshots with reasons and optimistic concurrency conflicts. Saving unchanged content with a current version does not add a revision; meaningful concurrent edits still return a conflict. Append-only updates/comments have UTC timestamps. Event dates remain separate from recording/edit dates.
- Selected-entry (the default) or full-timeline read-only shares. Full-timeline shares include future entries. Email invitations require authentication, matching verified email and explicit acceptance; the current verified email is rechecked on subsequent reads. Disabling an owner suspends their shares until the account is enabled or the grants are permanently revoked. Bearer links use random 256-bit tokens stored only as hashes and protected HttpOnly access cookies; expiry/revocation is checked for every read/download. Links are shown once and can be regenerated by creating a new grant. A browser holds one link grant at a time. Accepted invitations remain available in Settings. Shared-entry Back navigation returns to the shared timeline.
- Owner-only PDF/ZIP exports for all, selected or date-filtered records. PDF includes descriptions, updates, revisions, timestamps and attachment references. ZIP contains `report.pdf`, collision-safe original files and `manifest.json` with entry IDs and SHA-256 hashes; file integrity is checked during copying. Guests cannot export. PDF text is normalized for readable reporting; original rich-text styling and embedded screenshots are represented as text/file references.
- Soft removal hides entries from the timeline, sharing and export. It retains revision/file data for operator-managed retention. There is no self-service permanent account deletion.

## Storage and operational limits

| Configuration | Default / behavior |
| --- | --- |
| `Storage:Root` | `ClearParentLite/App_Data/uploads`, outside `wwwroot`; may be an absolute private path |
| `Storage:MaxFileBytes` | 20 MiB per file; endpoint request ceiling is 22 MiB |
| `Storage:QuotaBytes` | 1 GiB per uploader; SQL Server transaction-owned application locks serialize uploads per owner to enforce quotas and safe retries |
| `Storage:ScannerExecutable` | Absolute path to a malware scanner command accepting the file path as its only argument and returning 0 for clean files |
| `Storage:RequireMalwareScan` | Defaults to true outside Development; uploads fail closed without a scanner |
| Export bounds | 500 entries; 2 million UTF-16 text characters including titles/reasons/history; 5,000 combined revisions/comments and 5,000 files; 250 MiB of files; one generator per process; 60-second cancellation budget checked between stages |
| Share lifetime | 1–90 days, default 7 |

Use a dedicated private directory accessible only to the app service account. Metadata uses opaque random storage keys and normalized display filenames. Signatures are checked, common active PDF/Office content is rejected, DOCX containers are bounded, and malware scanning is enforced by default in production. Signature/static checks are defense in depth and cannot replace a scanner, especially for legacy DOC or obfuscated PDF content. Office/PDF files download as attachments; only raster images render inline. Database failure compensates by removing the just-written file. A process crash between file write and metadata commit can leave an orphan: reconcile storage keys against attachment metadata during maintenance after backups, without deleting unknown files automatically.

Exports are built in private `App_Data/exports` files with delete-on-close, including cancellation/failure cleanup. A hard process failure may leave stale files depending on OS behavior; include this directory in private temporary-file maintenance. Size/history/file-count bounds are checked in SQL before loading report text, using DATALENGTH so trailing whitespace cannot bypass limits. A short serializable read transaction keeps entry/history/file metadata consistent across split queries and finishes before rendering or file copying. Corrupt or missing original files stop ZIP generation with a clear conflict response and clean temporary output. PDF layout generation itself is synchronous, so cancellation is observed before/after rendering rather than forcibly terminating a render.

## Production preparation

No deployment has been performed. The current PDF font configuration supports Windows hosts with Arial installed. Linux/container hosting requires a custom PDFsharp font resolver and redistributable font assets before use.

1. Set an explicit production SQL Server connection string; startup rejects the development LocalDB default outside Development. Use least-privileged database credentials and an encrypted SQL connection with a trusted certificate. Apply reviewed migrations with an operator identity before running the app; normal startup does not automatically migrate.
2. Set the real HTTPS `App:PublicOrigin`, explicit `AllowedHosts`, SMTP settings and optional Google credentials. Keep the app in Production. Serve HTTPS directly or configure a trusted reverse proxy with an explicit forwarded-header allowlist before enabling forwarded headers. Do not trust arbitrary client forwarded headers.
3. Configure the scanner and private storage service-account ACLs. Keep the production `RequireMalwareScan` default enabled. Protect the database, files, ASP.NET data-protection keys, and backup copies with host/disk encryption and access controls. Persist the key ring across restarts/instances and protect it with the host's supported key-at-rest mechanism. Keys are needed to validate cookies and Identity tokens.
4. Back up SQL and private files together. Test restores. The MVP is intended for a single app instance with local storage; multi-instance hosting needs shared private storage, a shared protected key ring and distributed export throttling. Upload quota/retry locks are coordinated by SQL Server.
5. Establish and disclose retention before real use: suggested configurable operator policy is 90 days for audit/security events and a documented account-deletion grace period of 30 days, adjusted to actual obligations. The app intentionally does not schedule destructive purges. Use a reviewed maintenance procedure covering entries, revisions, updates, attachments, grants, Identity data, audit data and backups. No IP addresses or content bodies are recorded in audit metadata. Record the policy in the deployment's privacy information and contact channel.
6. Suppress/redact full bearer-link paths, confirmation/reset query strings and sensitive filenames in reverse-proxy/access/analytics logs. The app sets no-store and no-referrer on responses and does not log token values intentionally. Keep framework request logs at Warning for private routes. Recipients can retain downloaded copies after revocation.
7. Provision an administrator only through a reviewed database/Identity administration process after verifying the operator's email; never grant Administrator during public signup. Administrator status provides user operations, not record access. Static site settings are managed through secure configuration; there is no CMS.

## Verification

```powershell
dotnet build
dotnet test
dotnet list package --vulnerable --include-transitive
```

Tests use real SQL Server LocalDB, real Identity cookies and CSRF, and isolated `ClearParentLiteTest_<random>` databases that fixtures remove. SQL Server LocalDB must be accessible to the executing Windows identity. In restricted automation environments this may require running the approved test command outside the sandbox. The suite covers ownership attacks, revisions/concurrency, filtering, safe rich text/files, invitations/link scope/expiry/revocation, downloads, guest restrictions, scoped exports and ZIP integrity.

The optional browser acceptance script requires Microsoft Edge and Python Playwright. Tooling can be installed locally without changing application dependencies:

```powershell
python -m pip install --target .test-tools playwright pymupdf
# Start the HTTPS Development app in another terminal, then:
python tests/browser_smoke.py
```

It registers a unique disposable development account, consumes its local pickup email, creates test records, pastes an image, uploads DOCX/PDF, edits/adds an update, searches, checks mobile overflow, shares/revokes and downloads ZIP. It also verifies bold/bullet formatting after reload, simulated expired-session recovery, lost create/upload response retries without duplicates, modal discard protection, and every main workspace surface at 390px and 1440px. It saves ignored QA screenshots/reports under `artifacts/qa`. It does not run against remote origins. Remove test accounts only through a reviewed disposable-database cleanup if desired.

## Reviewing this branch

GitHub branch: https://github.com/benstieglitz00/ClearParentLite/tree/feature/mvp-foundation

1. Check out `feature/mvp-foundation` and follow the Windows setup above. No database schema changes were required by the quality-review fixes.
2. Open https://localhost:7273 and register a review account. Follow its confirmation link from the private local email pickup directory, then sign in.
3. Press `N`, enter a title/date/description, try bold and bullet formatting, paste a screenshot, and attach a PDF or DOCX. Save, reload and verify the image remains visible. Edit the entry, expand revision history, add an update/comment, and search for its text with an inclusive date range.
4. Create a selected-entry link from Sharing and open it in an incognito browser. Verify read-only controls and permitted file downloads. Revoke access from the owner browser and refresh the guest view; access is denied. An invitation requires a second registered, confirmed account with the invited email.
5. Export selected records as PDF and ZIP. ZIP includes the report, original attachments and manifest. Try mobile browser emulation; navigation, editor, sharing and exports remain usable at 390px.

Quality-review results: Release build passed with **0 warnings/errors**; **34 automated tests passed, 0 skipped**; EF reports no pending model changes; NuGet reports no known vulnerable packages in either project (2026-10-31). The expanded browser workflow passed, including modal protection, image persistence and interrupted/truncated-response recovery. Desktop/mobile screenshots and the exported PDF were visually inspected; all three original ZIP attachments matched the manifest hashes and lengths.

The review added regression coverage for duplicate/concurrent submissions, valid-file replay versus changed payloads, per-owner quota races, owner disabling, current invitation-email verification, administrator isolation, deletion visibility, account lockout/rate limits, malformed input/cookies/Office XML, inclusive filtering/pagination, corrupt/missing export files and SQL export-size bounds. Sharing/export forms progressively show only controls relevant to their scope; server validation remains authoritative. Historical development records created before block-text normalization may require re-saving before phrase searches spanning separate paragraphs match.

Remaining production requirements are real SQL/HTTPS/host configuration, SMTP delivery verification, malware scanning and private-storage ACLs, protected/persistent data-protection keys, backup/restore and retention/deletion procedures. Google credentials and live callback testing are required before enabling Google login. Windows PDF fonts are supported; Linux needs a font resolver. There is no automatic email retry/outbox, permanent-deletion workflow, multi-instance export throttle or forced PDF-render timeout. No merge or deployment is authorized by the branch push.

See `IMPLEMENTATION_PROGRESS.md` for the actual verification record, milestones and external setup requirements.
