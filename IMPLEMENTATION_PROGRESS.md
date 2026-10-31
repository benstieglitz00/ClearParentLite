# ClearParent Lite implementation

Branch: `feature/mvp-foundation`. Specification: `ClearParentLite-MVP-Spec.md`.
Original repository inspection: pristine ASP.NET Core MVC .NET 10 starter; baseline build passed. No existing application or database was modified.

## Checklist
- [x] 1. Foundation: SQL Server EF/Identity schema, migrations, DI and test project.
- [ ] 2. Public pages, local/Google authentication, confirmation/reset and roles.
- [ ] 3. Owner-scoped timeline, filters, CRUD, revisions and concurrency.
- [ ] 4. Rich editor, private attachments, validation and paste uploads.
- [ ] 5. Append-only updates and revision history.
- [ ] 6. Selected/timeline shares, verified invitations, expiry and revocation.
- [ ] 7. PDF/ZIP export, audit, security, responsive UI and documentation.

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

## Resume notes
Foundation complete. Identity/timeline/file backend started; UI, integration tests, sharing and export remain in progress. Use HTTPS launch profile; configured origin must match port 7273.
