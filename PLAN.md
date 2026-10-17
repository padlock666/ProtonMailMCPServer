# Implementation plan and progress

## Current state

Design approved on 2026-10-17. Phase 1 foundation is implemented and its offline acceptance criteria pass: .NET 10 solution, pinned dependencies and lock files, configuration/secret/certificate validation, stdio host, and synthetic tests. No mailbox adapter or MCP tools exist yet. No Bridge connection or live-mail validation has been performed.

**Next implementation phase: Phase 2, when requested.** Phase 1 version-control handoff remains pending because this workspace has no Git repository metadata. These checklists are a work ledger; only recorded checks establish evidence. Follow the scope of the user's implementation request; do not assume permission to broaden the MVP or access a personal mailbox.

Read [DESIGN.md](DESIGN.md), [SECURITY.md](SECURITY.md), and [the testing guide](docs/TESTING.md) before implementing. Routine implementation details within these requirements do not need repeated approval. Surface any proposed change to the approved behavior or safety boundaries.

## Phase 1 — Foundation

Deliverables:

- [x] Create `ProtonMailMcp.slnx`, one `net10.0` console application, and one xUnit test project using the documented layout.
- [ ] Select and record compatible exact versions of the official MCP SDK, hosting dependencies, MailKit/MimeKit, and test packages. Commit SDK selection and NuGet lock files. **Versions selected, files generated and verified; Git commit pending (no `.git` metadata in the supplied workspace).**
- [x] Configure nullable reference types, explicit stdio hosting, dependency injection, and stderr-only diagnostic logging.
- [x] Implement configuration validation, public-certificate loading, and owner-only password-file loading without connecting to Bridge.
- [x] Establish synthetic test fixtures and build/test commands. Add appropriate source-control exclusions for generated output and local secrets before creating local configuration.
- [x] Update development documentation to distinguish actual commands from remaining planned behavior.

Acceptance criteria:

- [x] Restore, Release build, and offline unit tests pass from the workspace root.
- [x] Invalid/missing configuration, non-loopback addresses, invalid TLS modes, empty allowlists, unusable certificate files, and unsafe password-file permissions fail with sanitized diagnostics.
- [x] MCP initializes over stdio without Bridge running; stdout remains protocol-only.
- [x] No real secrets, mailbox data, SMTP setup, HTTP listener, or mutation capability is introduced.

## Phase 2 — Read-only mailbox adapter

Deliverables:

- [ ] Implement the small `IReadOnlyMailbox` interface and DTOs behind a MailKit adapter.
- [ ] Add per-operation connections, serialized access, cancellation/deadline propagation, TLS pinning, and safe cleanup.
- [ ] Implement allowlisted folder discovery, typed header/date/unread searches, and bounded UID-based summary retrieval.
- [ ] Implement UIDVALIDITY validation, body-structure-first text selection, text-part-only retrieval, HTML conversion, and attachment metadata.
- [ ] Add protocol integration tests against a scripted local fixture plus MIME/validation unit tests.

Acceptance criteria:

- [ ] Actual adapter traffic uses `EXAMINE` and `BODY.PEEK`, with no read/write selection or mutating commands on success, failure, cancellation, or disconnect.
- [ ] Allowlist denial occurs before forbidden folder retrieval; stale and missing UIDs return distinct safe errors.
- [ ] Certificate mismatch/expiry and missing required STARTTLS fail before authentication; no fallback occurs.
- [ ] Default/maximum results, date boundaries, UID ordering, transfer limits, output truncation, and attachment-payload exclusion are verified.
- [ ] Concurrent calls, timeouts, malformed MIME, disappearing messages, and broken connections preserve safety and release resources.
- [ ] All automated adapter tests run without a Proton account or live Bridge.

## Phase 3 — MCP interface

Deliverables:

- [ ] Explicitly register `list_folders`, `search_messages`, and `get_message` with useful descriptions and read-only/non-destructive annotations.
- [ ] Publish structured input/output contracts and sanitized tool error handling using the chosen SDK.
- [ ] Add process-level SDK client tests for discovery, schemas, results, failures, cancellation, and stdout hygiene.
- [ ] Update documentation with the implemented contracts and a secret-free stdio launch example.

Acceptance criteria:

- [ ] The official SDK client discovers exactly the three approved tools and successfully calls each using synthetic IMAP data.
- [ ] Contract tests cover optional filters, input rejection, metadata results, content statuses, and truncation indicators.
- [ ] Denied folders, stale references, TLS/authentication failures, Bridge outages, and timeouts are communicated without leaking raw exceptions or mailbox data.
- [ ] Diagnostics do not corrupt stdio, and server termination/cancellation releases resources without expunging.
- [ ] Unit, protocol, and MCP process suites pass without live credentials.

## Phase 4 — Bridge validation and handoff

Deliverables:

- [ ] Implement the opt-in live suite described in [docs/TESTING.md](docs/TESTING.md), requiring an operator-prepared folder and fixture identities.
- [ ] Document Linux/Bridge setup, public-certificate export and replacement, external credentials/configuration, VS Code build/debug workflow, and stdio client launch.
- [ ] Document safe troubleshooting for unavailable Bridge, changed credentials/certificates, stale UIDs, limits, and unsupported content.
- [ ] Record tested SDK/package/Bridge/Linux versions and actual verification outcomes without mailbox data.

Acceptance criteria:

- [ ] Real Bridge tests preserve persistent flags and message membership across repeated searches/reads, including unread messages and pre-existing deleted flags.
- [ ] Restart, connection loss, incorrect credentials, and changed certificates fail/recover as specified without fallback or mailbox mutation.
- [ ] A fresh setup can follow the documented workflow successfully, with secrets outside the repository and only public certificate material supplied to the application.
- [ ] The complete automated suite passes, and the live evidence is recorded. If live access is unavailable, leave this phase incomplete and explicitly record the limitation.

## Definition of done

All four phases meet their acceptance criteria; documentation matches actual behavior; the intended Linux/Bridge combination has live evidence; no excluded feature or write capability is present. Read-only annotations alone, mocked adapter tests alone, or a successful build alone do not establish completion.

## Evidence ledger

Update this table after actual implementation/verification. Record date, phase, commands or test filters, versions, results, and limitations. Do not include message subjects, addresses, UIDs, passwords, personal paths, or protocol dumps.

| Date | Phase | Evidence | Result / limitation |
| --- | --- | --- | --- |
| 2026-10-17 | Preparation | Workspace inventory and approved design captured as documentation | Documentation only; implementation and all runtime acceptance pending |
| 2026-10-17 | Preparation | Seven Markdown files checked for valid local links, balanced code fences, final newlines, and trailing whitespace; source/project file inventory checked | Passed; no application or project files present; no runtime tests exist or were run |
| 2026-10-17 | Phase 1 | `dotnet --info`; NuGet publication/target-framework/dependency metadata and restored SDK XML documentation reviewed | SDK 10.0.112 (roll-forward disabled), runtime 10.0.12; MCP/Core 2.2.0, Hosting 10.0.13, MailKit/MimeKit 4.18.1, xUnit 2.9.3, VS adapter 4.0.1, Test SDK 18.11.0; complete graphs in two lock files |
| 2026-10-17 | Phase 1 | Initial `dotnet restore ProtonMailMcp.slnx`; subsequent restore after patched dependency override | Initial restore failed audit for transitive PKCS 10.0.0 (GHSA-2fmv-rv2c-59x7 and GHSA-846f-f764-76mx). Added direct exact PKCS 10.0.13 override; restore passed without suppressing audit or warnings |
| 2026-10-17 | Phase 1 | `dotnet restore ProtonMailMcp.slnx --locked-mode --packages /tmp/protonmcp-packages` | Passed for both projects with full-graph audit enabled; NuGet network access used, no offline package restore claimed |
| 2026-10-17 | Phase 1 | `dotnet build ProtonMailMcp.slnx --configuration Release --no-restore` | Passed, zero warnings/errors. Initial sandbox build blocked MSBuild/compiler IPC; single-node build with shared compilation disabled also passed. Normal documented build passed with runner IPC permission |
| 2026-10-17 | Phase 1 | `dotnet test ProtonMailMcp.slnx --configuration Release --no-build --filter 'Category!=LiveBridge'` | Final run: 63 passed, 0 failed, 0 skipped. Initial sandbox run aborted before tests because VSTest control sockets were denied; completed run granted runner IPC. Tests use synthetic files and child-process stdio only; no mailbox gateway access |
| 2026-10-17 | Phase 1 | Unit and MCP tests, category discovery, final source/configuration review | Missing/invalid settings, canonical loopback, TLS-mode rejection, exact allowlist, invalid/date-expired/future/public-only certificates, unsafe password permissions/private parent, symlinks, FIFO/directory, size/encoding bounds, opened-handle replacement, cancellation, sanitized errors, official SDK initialization, protocol-only stdout and EOF shutdown pass. Foreign-owner rejection is implemented via opened-handle UID checks but lacks a privilege-dependent integration case |
| 2026-10-17 | Phase 1 | Markdown local-link/fence/whitespace/newline checks (seven documents); lock-file PKCS checks; application source scan for network/mail/HTTP/tool-discovery/stdout logging entry points | Passed; no mailbox connection, mutation, SMTP setup, HTTP listener, real credentials, or mailbox data introduced. SDK/framework logs disabled; fixed diagnostics use stderr. Source-control exclusions present. `git status` confirms supplied directory is not a Git repository, so files are ready for version control but no commit was made |

Phase 1 verification used `DOTNET_CLI_HOME=/tmp/protonmcp-dotnet` for writable tool state. The source/lock files use no machine-specific paths. Local config placement outside the checkout is an operator requirement, not runtime repository-root detection. Linux `statx` is required and fails closed if unavailable. This phase proves local validation/stdio behavior only: peer TLS pinning, adapter protocol safety, tool contracts, and real Bridge compatibility remain unverified until their respective phases. The MVP is incomplete.

## Deferred work

HTTP/remote hosting, multi-account operation, attachment downloads, body search, pagination, indexing/caching, and background synchronization require a separate product decision. Sending or mailbox mutation is outside this product's read-only purpose, not a later MVP phase.
