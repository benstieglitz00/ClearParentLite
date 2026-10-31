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
- No deployment, push, merge or original ClearParent changes are authorized.

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
- No deployment, push or merge performed. Original ClearParent application untouched.

## Resume notes
All seven implementation phases are complete with the external setup/limitations above. Setup and operational instructions are in README.md. Final Release build: 0 warnings/errors; final suite: 19/19; browser acceptance rerun after concurrency/layout changes: passed. Work is committed on the dedicated feature branch. Do not push or deploy without user approval.
