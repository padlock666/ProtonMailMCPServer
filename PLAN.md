# Implementation plan and progress

## Current state

Design approved on 2026-10-17. Phases 1 and 2 are implemented with passing offline acceptance: the .NET 10 foundation and a read-only MailKit adapter verified against synthetic TLS/IMAP fixtures. No MCP tools exist yet. No Bridge connection or live-mail validation has been performed.

**Next implementation phase: Phase 3, when requested.** SDK/package pins and lock files are now tracked in the supplied Git baseline; the earlier missing-metadata handoff limitation is resolved. No Phase 2 commit has been made. These checklists are a work ledger; only recorded checks establish evidence. Follow the scope of the user's implementation request; do not assume permission to broaden the MVP or access a personal mailbox.

Read [DESIGN.md](DESIGN.md), [SECURITY.md](SECURITY.md), and [the testing guide](docs/TESTING.md) before implementing. Routine implementation details within these requirements do not need repeated approval. Surface any proposed change to the approved behavior or safety boundaries.

## Phase 1 — Foundation

Deliverables:

- [x] Create `ProtonMailMcp.slnx`, one `net10.0` console application, and one xUnit test project using the documented layout.
- [x] Select and record compatible exact versions of the official MCP SDK, hosting dependencies, MailKit/MimeKit, and test packages. Commit SDK selection and NuGet lock files. **Verified unchanged and tracked in the supplied Git baseline during Phase 2; the historical missing-metadata limitation below no longer applies.**
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

- [x] Implement the small `IReadOnlyMailbox` interface and DTOs behind a MailKit adapter.
- [x] Add per-operation connections, serialized access, cancellation/deadline propagation, TLS pinning, and safe cleanup.
- [x] Implement allowlisted folder discovery, typed header/date/unread searches, and bounded UID-based summary retrieval.
- [x] Implement UIDVALIDITY validation, body-structure-first text selection, text-part-only retrieval, HTML conversion, and attachment metadata.
- [x] Add protocol integration tests against a scripted local fixture plus MIME/validation unit tests.

Acceptance criteria:

- [x] Actual adapter traffic uses `EXAMINE` and `BODY.PEEK`, with no read/write selection or mutating commands on success, failure, cancellation, or disconnect.
- [x] Allowlist denial occurs before forbidden folder retrieval; stale and missing UIDs return distinct safe errors.
- [x] Certificate mismatch/expiry and missing required STARTTLS fail before authentication; no fallback occurs.
- [x] Default/maximum results, date boundaries, UID ordering, transfer limits, output truncation, and attachment-payload exclusion are verified.
- [x] Concurrent calls, timeouts, malformed MIME, disappearing messages, and broken connections preserve safety and release resources.
- [x] All automated adapter tests run without a Proton account or live Bridge.

## Phase 3 — MCP interface

Deliverables:

- [ ] Explicitly register `list_folders`, `search_messages`, and `get_message` with useful descriptions and read-only/non-destructive annotations.
- [ ] Publish structured input/output contracts and sanitized tool error handling using the chosen SDK.
- [ ] Add process-level SDK client tests for discovery, schemas, results, failures, cancellation, and stdout hygiene.
- [ ] Update [configuration](docs/CONFIGURATION.md) and [usage](docs/USAGE.md) documentation with the implemented tool contracts and verified client examples. The current guides cover configuration and initialization-only launch; tool usage remains pending.

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
| 2026-10-17 | Phase 2 | Pinned MailKit 4.18.1 stream-extension/fetch APIs reviewed against restored XML and [release source](https://raw.githubusercontent.com/jstedfast/MailKit/4.18.1/MailKit/Net/Imap/ImapFolderFetch.cs); exact versions and lock files unchanged | Implemented `IReadOnlyMailbox`, immutable DTOs, lazy singleton registration, serialized per-operation connections, strict TLS pinning before authentication, UID summaries, typed searches, BODYSTRUCTURE-first text reads, MIME conversion, and bounded sanitized results; no MCP features added |
| 2026-10-17 | Phase 2 | `DOTNET_CLI_HOME=/tmp/protonmcp-dotnet dotnet restore ProtonMailMcp.slnx --locked-mode --packages /tmp/protonmcp-packages -m:1` | Passed both projects with the populated package cache and existing audit policy; no dependency/lock changes and no offline-only NuGet claim |
| 2026-10-17 | Phase 2 | `dotnet build ProtonMailMcp.slnx --configuration Release --no-restore -m:1 -p:UseSharedCompilation=false`; final documented `dotnet build ProtonMailMcp.slnx --configuration Release --no-restore` | Passed, zero warnings/errors. Final normal build used runner IPC permission. Commands used the temporary CLI home described below |
| 2026-10-17 | Phase 2 | Final `dotnet test ProtonMailMcp.slnx --configuration Release --no-build --filter 'Category!=LiveBridge' --logger 'trx;LogFileName=phase2.trx' --results-directory /tmp/protonmcp-phase2-results` | **128 passed, 0 failed, 0 skipped** (83 Unit, 40 Protocol, 5 Mcp); runner IPC and synthetic loopback sockets permitted. Initial sandbox test invocation failed on MSBuild IPC before tests could run. All mailbox sessions use ephemeral scripted fixtures, not Bridge |
| 2026-10-17 | Phase 2 | Actual-adapter ordered protocol scripts admit only required session/read commands, then require disconnect; recovery sessions and pending-connection assertions | EXAMINE only, read-only access confirmation, UID-only requests, BODY.PEEK only, text-section-only fetches, no attachment payloads or full-message retrieval, no CLOSE/expunge/mutation. Evidence includes success, rejected EXAMINE/read-write response, rejected fetch, malformed reply, broken connection, authentication failure, stale/missing/vanished messages, mid-literal cancellation, queued cancellation/timeout, search deadline, and stalled LOGOUT |
| 2026-10-17 | Phase 2 | Synthetic required STARTTLS and TLS-on-connect sessions; wrong/expired/future peer pins; missing STARTTLS; failure followed by a fresh connection | TLS failures occur before authentication with no fallback/retry. Exact folder/descendant denial and empty allowlist fail before a connection; discovery omits missing/nonselectable/unapproved entries. Serialized operations release the gate after errors/cancellation; a controlled clock proves the production 30-second timer is armed before queue acquisition |
| 2026-10-17 | Phase 2 | Protocol limit/search/content fixtures plus MIME/validation unit cases | Default 20/maximum 50 results, descending matching UIDs, truncation, To/Cc recipient OR within AND filters, delivery-date start/end, unread tri-state, absent metadata, exact 1 MiB transfer, advertised/actual oversize, 32,000-character output, 100-attachment metadata cap, alternative/related selection, attached text/nested-message exclusion, strict transfer/charset decoding, empty/malformed/unsupported bodies, controls/surrogates, and inert HTML references pass |
| 2026-10-17 | Phase 2 | Regression failures investigated during implementation | Tests caught an empty top-level part specifier producing full-message PEEK, MailKit absorbing LOGOUT cancellation, and a top-level attached text candidate. Fixed with explicit TEXT section, post-logout deadline check, and candidate ancestry/attachment validation. Final regressions pass; no safety boundary was relaxed |
| 2026-10-17 | Phase 2 | Markdown links/fences/newlines/whitespace across seven documents; C# source whitespace/newline checks; `git diff --check`; source review and Git pin/lock inventory | Passed. No adapter protocol logger, stdout logging, SMTP, mutable application interface, real secrets/mail, or live-mail access introduced. Existing initialization/stdout process tests still pass. Pin/lock files are tracked in the supplied baseline; no new commit requested or created |
| 2026-10-17 | Documentation | Added `docs/CONFIGURATION.md` and `docs/USAGE.md`; linked both from README; replaced duplicate operator instructions in the development guide with references and same-task maintenance requirements | Covers implemented settings, file permissions, TLS pins, allowlists, build/stdio launch, diagnostics, adapter limits, and current initialization-only capability. Phase 3 tools and Phase 4 live acceptance remain incomplete; no application code or mailbox access added in this documentation task |
| 2026-10-17 | Documentation | Python checks across nine Markdown documents: local links/anchors, balanced fences, final newlines, whitespace, JSON parsing, sample keys matched to `ConfigurationDocument`, documented diagnostic codes matched to source; `git diff --check` | Passed. Commands and configuration behavior reviewed against source and existing tests; runtime/build tests were not rerun for this documentation-only change |
| 2026-10-17 | Documentation | Added `examples/config.example.json`; linked from README/configuration guide; development workflow requires keeping the template synchronized | JSON parsing, equality with the documented example, configuration key/absolute-path/placeholder checks, Markdown links/fences/whitespace, and `git diff --check` passed. Template is not ignored and contains placeholders only; no secret files created, runtime tests rerun, or mailbox contacted |

Phase 1 verification used `DOTNET_CLI_HOME=/tmp/protonmcp-dotnet` for writable tool state. The source/lock files use no machine-specific paths. Local config placement outside the checkout is an operator requirement, not runtime repository-root detection. Linux `statx` is required and fails closed if unavailable. Phase 1 proved local validation/stdio behavior only. Phase 2 now proves peer pin enforcement and adapter protocol safety against synthetic fixtures. Tool contracts and real Bridge compatibility remain unverified until Phases 3 and 4. MailKit metadata parsing and large matching UID sets are not promised constant-memory; transfer and returned-result bounds are enforced. The MVP is incomplete.

## Deferred work

HTTP/remote hosting, multi-account operation, attachment downloads, body search, pagination, indexing/caching, and background synchronization require a separate product decision. Sending or mailbox mutation is outside this product's read-only purpose, not a later MVP phase.
