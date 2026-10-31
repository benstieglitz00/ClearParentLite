# ClearParent Lite — MVP Implementation Specification
Version 1.0 | 2026-10-31

## Mission
Build a public parenting-resource site and a private, secure, Linear-inspired documentation timeline. A parent can record an event with title, event date, rich-text description, pasted screenshots, and files, then later edit with preserved revisions, add timestamped updates, search, share selectively, and export.

## Locked product decisions
- Public registration; Google OAuth plus email/password login and password reset.
- Roles: Administrator, Parent, Read-only Guest. Roles do not supersede record-level permissions.
- Public homepage: hybrid product introduction and featured parenting resources. Static pages for MVP. Nav: Home, Resources, Login, Sign Up.
- One timeline per parent, compact Linear-like list, ordered by event date descending with stable ID tie-breaker.
- New entry: title, description, event date; rich-text editor supporting Ctrl+V screenshots. All three fields required; event date may default to today but user can change it.
- Supported attachments: common image formats, PDF, DOC/DOCX. Store original files privately on local disk through a storage abstraction.
- Entry descriptions editable with immutable revisions; timestamped updates/comments separately; update revisions preserved if edits allowed. Show event date and separately created/modified timestamps.
- Search: keywords in entry title/description and updates plus event-date range. Attachment text indexing is out of scope.
- Sharing: selected entries or full timeline, via invited email accounts and revocable read-only links. Guests can view only and download only permitted attachments.
- Export: PDF timeline report and ZIP containing the PDF and original permitted attachments.
- SQL Server + EF Core. ASP.NET Core MVC and Razor. Keep original ClearParent repo untouched.

## Architecture
Single ASP.NET Core MVC application in existing ClearParentLite solution. ASP.NET Core Identity for local accounts, external Google OAuth, email confirmation and reset flows. EF Core SQL Server for relational persistence and migrations. Use services/interfaces for entry management, attachment storage, share authorization, PDF/ZIP export, and email delivery. Use Razor views with progressive enhancement for fast entry modal and inline updates; avoid introducing a separate SPA unless necessary. Choose a maintained rich-text editor with paste-image support; sanitize stored HTML on the server with an allowlist. Use environment-specific configuration and user secrets for credentials. Local storage root outside wwwroot; expose downloads only through authenticated/authorized controller actions. Use UTC for stored audit timestamps; store event date as date-only. Use server-side pagination and indexes.

## Data model
- ApplicationUser: Identity fields; display name, createdUtc, disabledUtc nullable.
- TimelineEntry: Id GUID, OwnerUserId FK, Title, DescriptionHtml (sanitized), EventDate (date), CreatedUtc, UpdatedUtc, IsDeleted, RowVersion; index (OwnerUserId, EventDate DESC, Id).
- EntryRevision: Id, EntryId, RevisionNumber, Title, DescriptionHtml, EventDate, EditedByUserId, EditedUtc, ChangeReason nullable. Immutable snapshots; initial revision created with entry.
- EntryUpdate: Id, EntryId, AuthorUserId, BodyHtml, CreatedUtc, UpdatedUtc nullable, IsDeleted; optional revisions if update editing enabled. MVP can make updates append-only.
- Attachment: Id, EntryId, EntryUpdateId nullable, UploadedByUserId, OriginalFileName (display only), StorageKey (random, opaque), ContentType, ByteLength, Sha256, CreatedUtc, IsDeleted; never use client filename as path.
- ShareGrant: Id, OwnerUserId, Scope (Timeline|SelectedEntries), AccessMethod (Invitation|Link), InvitedEmailNormalized nullable, InvitedUserId nullable, TokenHash nullable, CreatedUtc, ExpiresUtc nullable, RevokedUtc nullable, CreatedByUserId; permission is always ReadOnly.
- ShareGrantEntry: ShareGrantId, EntryId for selected-entry shares.
- AuditEvent: Id, ActorUserId nullable, OwnerUserId, Action, TargetType, TargetId, OccurredUtc, MetadataJson (non-sensitive), IpAddress optional with retention policy. Record sharing, revocation, export, download, and login security events.
- Optional OutboxEmail for reliable invitation/reset mail delivery.

Apply tenant/owner scoping in every query. Sharing authorization must be checked server-side for entries, updates, attachments, searches, and exports. No public file URLs.

## Authentication and access
- ASP.NET Core Identity with secure password policy, email verification before sensitive actions, account lockout/rate limits, anti-forgery protection, secure HTTP-only SameSite cookies, HTTPS, and security headers.
- Google OAuth via secrets; link external identity carefully to verified account; never auto-link by unverified email.
- Administrator can manage static site operational settings and users but must not automatically read parents' private entries. Parent owns timeline and shares. Read-only Guest accesses only explicit active grants. Invitation acceptance requires authenticated matching verified email. Link shares use cryptographically random bearer tokens stored only as hashes, with expiry/revocation; clearly warn that anyone holding the link may view the selected records. No anonymous indexing; no-cache headers.
- Account deletion/retention and audit policies should be configurable/documented; avoid claims of legal evidentiary certification.

## UI routes and behavior
- Public: /, /resources, /account/register, /account/login, /account/forgot-password.
- Authenticated: /timeline, /entries/new, /entries/{id}, /entries/{id}/edit, /entries/{id}/updates, /shares, /exports, /settings.
- New Entry is prominent and keyboard-friendly. Modal with title, event date, rich text editor, drag/drop and paste images, upload progress, Save/Cancel; prevent duplicate submissions. Preserve unsaved content on validation error.
- Timeline rows show title, event date, update count, attachment count, modified indicator. Search keyword and inclusive date-range filters, pagination. Sort event date descending; use createdUtc/id as stable tie-breakers.
- Detail view: description, attachments, timestamped update stream, add-update box, edit action, revision history and sharing action. Clearly distinguish 'Occurred on' vs 'Recorded on' vs 'Edited on'.
- Shared view is read-only: no edit/create controls; all mutation endpoints independently deny access.
- Homepage: hero, how it works, featured static resources, clear signup/login CTA. Static resource pages use shared layout; no admin CMS editing in MVP.

## File handling
- Accept allowlisted extensions AND verified content signatures where feasible; limit per-file size (initial configurable 20 MB) and total upload/request size; reject executable and active content; normalize filenames for display, random storage keys, scan files with AV when available before distribution. Restrict PDF/Office handling to downloads or safe previews. Strip active HTML, disallow remote tracking images and unsafe URLs. Store under non-web-accessible private directory; enforce quota and authorization. Use transactional metadata + compensating cleanup for failures. Include SHA-256 checksums in export manifest. Never expose filesystem paths.

## Export
- Owner can choose entire timeline, date range, or selected entries; exported records ordered by event date and include title, event date, description, update history, created/edited timestamps, and attachment references. PDF generated on server. ZIP contains report.pdf, attachments/ with collision-safe names, manifest.json listing source entry IDs, original names, hashes, and export UTC time. Do not silently include entries beyond selected scope. Guests may export only if explicitly enabled later; MVP guest export disabled. Stream with size/time limits and cleanup temp files.

## Delivery milestones
1. **Foundation:** inspect existing repo and project before modifying; create branch feature/mvp-foundation; verify build, .gitignore, configuration, SQL Server connection, EF migrations, DI, test project. No secrets committed.
2. **Identity + public pages:** register/login/logout, Google OAuth configuration, email verification/reset via configurable provider, roles, public static homepage/resources, integration tests.
3. **Timeline CRUD:** schema/migrations, ownership checks, list/search/date filtering, create/edit/view, immutable entry revisions, concurrency handling, tests.
4. **Rich editor + files:** paste screenshot, upload image/PDF/DOCX, private storage, authorization and file validation, tests.
5. **Updates + history:** append-only timestamped updates, revision history UI, correct event vs audit dates, tests.
6. **Sharing:** selected/full scope, invitations and read-only links, expiration/revocation, guest UI, cross-user security tests.
7. **Export + hardening:** PDF+ZIP, audit events, rate limits, security checks, accessibility and responsive polish, end-to-end tests, README setup/deployment instructions.

## Acceptance criteria
- A new user can register, verify email, login/logout, reset password; Google login works when provider credentials configured. App fails gracefully with clear configuration instructions if credentials absent.
- Authenticated parent creates entry with title, event date, formatted description, pasted screenshot, and uploaded PDF/DOCX; refresh retains content and attachment access.
- Timeline lists only own entries, newest event date first; keyword/date filters and pagination work; updating description preserves immutable before/after revision history; timestamped updates remain in order.
- User A cannot read/search/edit/download/export User B's private data by guessing URLs or IDs; tests cover each endpoint and indirect references.
- Selected-entry link reveals only selected entries; full-timeline share reveals permitted current entries; expiration/revocation immediately prevents future access; guest cannot mutate via HTTP even if UI controls are hidden.
- Private attachments cannot be accessed through static URLs; invalid files, oversize files, and unsafe rich text are rejected/sanitized.
- Owner exports selected/date-filtered records as a readable PDF and ZIP containing original permitted files and manifest; filenames are safe and hashes match.
- Site builds from a fresh checkout, migrations apply, tests pass, no secrets are checked in, and README documents setup for SQL Server, Google OAuth, email delivery, file storage, and tests.

## Explicitly out of scope for MVP
Native mobile apps; collaborative co-editing; co-parent messaging; calendar/scheduling; AI summaries; OCR or attachment full-text indexing; court-ready certification; payments/subscriptions; full CMS administration; advanced permission matrix; public discovery of private timelines.

## Codex execution instructions
Inspect current solution and repository before editing. Implement one milestone at a time with minimal, reviewable commits on feature branches; never push directly to main or merge without user approval. Run builds and tests after each milestone; fix failures; report changed files, migration commands, test results, blockers, and any manual configuration. Do not fabricate passing tests or silently skip security features. Avoid asking questions unless a decision is genuinely blocking; prefer documented safe defaults. Do not deploy production or provision paid services without approval.
