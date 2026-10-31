# ClearParent Lite implementation

Branch: `feature/mvp-foundation`. Specification: `ClearParentLite-MVP-Spec.md`.
Original repository inspection: pristine ASP.NET Core MVC .NET 10 starter; baseline build passed. No existing application or database was modified.

## Checklist
- [x] 1. Foundation: SQL Server EF/Identity schema, migrations, DI and test project.
- [x] 2. Public pages, local/Google authentication, confirmation/reset and roles.
- [x] 3. Owner-scoped timeline, filters, CRUD, revisions and concurrency.
- [x] 4. Rich editor, private attachments, validation and paste uploads.
- [x] 5. Append-only updates and revision history.
- [x] 6. Selected/timeline shares, verified invitations, expiry and revocation.
- [x] 7. PDF/ZIP export, audit, security, responsive UI and documentation.

## Decisions / external setup
- SQL Server LocalDB is the disposable development default; production requires an explicit connection string.
- SMTP and Google credentials stay in user secrets/environment. Development mail goes to a private local pickup directory; never show confirmation/reset tokens in HTTP responses.
- Guests cannot export. Updates are append-only. Private uploads are served only after current grant/owner authorization.
- User subsequently authorized pushing this feature branch and verified review fixes. No merge, deployment or original ClearParent changes are authorized.

## Verification log
- Baseline `dotnet build`: passed (0 warnings/errors).
- Initial migration generated and applied successfully to isolated `ClearParentLiteDev` LocalDB.
- Foundation build: 0 warnings/errors. Initial sanitizer/file/token tests: 8 passed.
- Dependency advisories resolved with HtmlSanitizer 9.2.1039 and explicit Pkcs 10.0.14.
- HTTP/SQL integration suite: 15 passed, including real Identity cookies, CSRF, registration/confirmation/reset, cross-user reads/mutations/search, revisions and stale-write conflicts. Google live credentials remain external setup.
- Expanded suite: 16 passed. Selected link scope, hidden entries/files, invitation matching and acceptance by Guest role, full-timeline future entries, expiry/revocation, ZIP/PDF generation, manifest/file hash integrity and cross-owner export denial covered.
- LocalDB access can fail under the sandbox's alternate identity; approved `dotnet test` outside the sandbox succeeds. Test databases have unique `ClearParentLiteTest_` names and are dropped by the fixture.
- Final Release suite: 19 passed, 0 skipped. Added production scanner fail-closed/cleanup, oversize/public-directory rejection, macro/remote-image validation, real simultaneous edits (one success/one 409, no duplicate revision) and simultaneous appends (both preserved).
- `dotnet build -c Release --no-restore`: passed. `dotnet ef migrations has-pending-model-changes`: no changes. `git diff --check`: passed.
- `dotnet list package --vulnerable --include-transitive`: no vulnerable packages reported in either project (2026-10-31).
- `python tests/browser_smoke.py`: passed against the Release build. Real registration/local email confirmation/login, keyboard-friendly modal, realistic pasted PNG, DOCX/PDF uploads, refresh persistence, edit, update, update-text search, 390px mobile overflow checks, selected share, revocation, and ZIP download.
- Desktop/home/mobile timeline/detail screenshots inspected. Generated PDF rendered and visually inspected; expected title, update, filenames and revision text verified. ZIP checksums verified by integration tests. QA files are ignored under `artifacts/qa`.

## External setup and explicit limitations
- Google OAuth client credentials and real SMTP credentials were not supplied. Secure configuration hooks and local pickup/test-mail providers are implemented; live provider delivery/login is not claimed tested.
- Production uploads require an installed/configured malware scanner. Development permits signature-validated files without AV; this does not certify them malware-free.
- PDF font configuration currently supports Windows with Arial. Linux/container operation requires a custom font resolver and redistributable fonts.
- New-entry uploads are staged after saving the entry; a partial failure leaves a saved entry and an in-browser retryable draft. Process crashes may leave private orphan files; operator maintenance is documented.
- Deletion is soft. Retention, permanent account deletion, backups and data-protection key lifecycle are operator-managed and documented; there is no automated purge/outbox worker or legal-certification claim.
- Report descriptions are normalized to readable text, with screenshot/file references; original files are included in ZIP. Export rendering has bounded input and cooperative cancellation, not forced process termination.
- Feature branch push was authorized in the follow-up quality review; no deployment or merge performed. Original ClearParent application untouched.

## Follow-up MVP quality review
- Verified `feature/mvp-foundation` and an initially clean working tree; pushed it to `origin` and set upstream tracking. Branch: https://github.com/benstieglitz00/ClearParentLite/tree/feature/mvp-foundation
- Reviewed public/account flows, owner/guest/admin authorization, current invitation identity, bearer-link revocation, storage and quotas, revision/comment integrity, filtering/pagination, exports and responsive layout.
- Fixed draft loss on successful HTTP redirects to login, and added unsaved-modal/navigation protection. Successful JSON save responses are required before leaving an editor.
- Added stable browser request IDs for entry creation, append-only comments and file uploads. Replays reuse records; different content or owner/scope reuse is rejected. Simultaneous replay and lost-response recovery are tested. No schema changes needed: request identifiers reuse existing GUID primary keys.
- Fixed Quill 2 bullet-list persistence by normalizing lists into semantic HTML, and added block separators to searchable text. Long valid filenames retain their extensions. Malformed/DTD-bearing Office XML is rejected safely; partial file-write failures clean their private file.
- Timeline rows now project counts and metadata in SQL. Sharing/export selectors avoid loading descriptions, and private details authorize before loading history; guest detail queries omit revisions. Shared Back links now return to the grant view.
- Sharing defaults to selected entries and preserves user choices after invalid input. Current recipient verified email and active owner account are checked on reads. All private routes carry noindex/no-store headers, malformed share cookies fail closed, and missing account-link parameters return 400.
- Uploads use per-owner SQL application locks to avoid quota races/deadlocks and serialize retry IDs without locking other parents' ranges.
- Export preflight checks text/count/file bounds in SQL before loading HTML, including trailing whitespace, and reads metadata consistently before rendering. Corrupt/missing files return a clear 409 and delete temporary output. Limits now also include 5,000 history records and 5,000 files.
- Final Release review suite: **34 passed, 0 skipped**. Build: **0 warnings/errors**. EF reports no pending model changes. NuGet advisory check: no known vulnerable packages in either project. `node --check` and `git diff --check` passed.
- Expanded browser run passed rich formatting/pasted-image persistence, creation/editing/revisions/comments/search, sharing/revocation/ZIP, expired-session preservation, lost create/upload responses and truncated JSON upload receipts without duplicates, and every main page at 390px/1440px. Modal discard protection is verified at both widths. Fixtures include a valid generated PDF and minimal valid DOCX package. All original ZIP hashes/lengths match the manifest.
- Visually inspected narrow mobile modal/editor/sharing/export layouts and the final PDF. Scope-specific form controls reduce clutter; labels/counts use human-readable names and singular/plural text. Final report text contains the expected entry, bullet text, screenshot reference, updates, revision history and all filenames.
- Production requirements remain external configuration/verification, AV, key/backup/retention operations and optional Google credentials. Windows fonts are the supported PDF host configuration. No production service was provisioned.

## Resume notes
All seven implementation phases and the follow-up MVP quality review are complete with the external requirements/limitations above. Verified fixes and updated documentation belong on `feature/mvp-foundation`; pushing this branch is explicitly authorized. Do not merge or deploy. Local review uses https://localhost:7273 with Development email pickup; README contains the review checklist and setup instructions.
