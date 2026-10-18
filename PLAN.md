# Implementation plan and progress

## Current state

Design approved on 2026-10-17. Phases 1–3 are implemented: the .NET 10 foundation, read-only MailKit adapter, and three approved MCP tools verified against synthetic TLS/IMAP fixtures through the official SDK client. Phase 4 opt-in tooling and handoff documentation are implemented; the automated live acceptance suite has not been run. On 2026-10-18, an operator-performed manual test against real Bridge succeeded for MCP initialization, tool discovery, folder listing, and a delivery-date-range search. Subsequent operator investigation found that imported messages could have old sender dates and newer internal dates, so this established search execution rather than the intended date behavior. The owner requested sender-date filtering on 2026-10-18; the implementation now uses the sender Date header with `sentStart`/`sentEnd`. Live verification of this replacement is pending.

**Current scope: owner-requested sender-date search change implemented and synthetically verified. Real Bridge verification of the new date filters remains pending. Full live acceptance still requires operator-prepared fixtures and explicit authorization.** SDK/package pins and lock files are now tracked in the supplied Git baseline; the earlier missing-metadata handoff limitation is resolved. No Phase 2 commit has been made. These checklists are a work ledger; only recorded checks establish evidence. Follow the scope of the user's implementation request; do not assume permission to broaden the MVP or access a personal mailbox.

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

- [x] Explicitly register `list_folders`, `search_messages`, and `get_message` with useful descriptions and read-only/non-destructive annotations.
- [x] Publish structured input/output contracts and sanitized tool error handling using the chosen SDK.
- [x] Add process-level SDK client tests for discovery, schemas, results, failures, cancellation, and stdout hygiene.
- [x] Update [configuration](docs/CONFIGURATION.md) and [usage](docs/USAGE.md) documentation with the implemented tool contracts and verified client examples. The guides now document implemented contracts, SDK calls, error codes, and cancellation/shutdown limits.

Acceptance criteria:

- [x] The official SDK client discovers exactly the three approved tools and successfully calls each using synthetic IMAP data.
- [x] Contract tests cover optional filters, input rejection, metadata results, content statuses, and truncation indicators.
- [x] Denied folders, stale references, TLS/authentication failures, Bridge outages, and timeouts are communicated without leaking raw exceptions or mailbox data.
- [x] Diagnostics do not corrupt stdio, and server termination/cancellation releases resources without expunging.
- [x] Unit, protocol, and MCP process suites pass without live credentials.

## Sender-date search change — authorized 2026-10-18

The operator reported imported messages whose sender dates differ from Bridge internal dates, then requested searching by sender date instead. No mailbox output is retained. The cause of the internal-date discrepancy and the Proton app's missing date display are not established.

- [x] Replace delivery-date queries with sender `Date` header queries (`SENTSINCE`/`SENTBEFORE`); no internal-date fallback.
- [x] Rename inputs to `sentStart`/`sentEnd`; reject obsolete `receivedStart`/`receivedEnd` instead of silently broadening searches.
- [x] Update tool descriptions, approved design, usage guide, and synthetic protocol/MCP regressions.
- [x] Complete synthetic verification and record commands/results (193 tests passed).
- [ ] Verify sender-date matching against real Bridge with operator authorization; previous live evidence covers the former delivery-date implementation only.

## Phase 4 — Bridge validation and handoff

Deliverables:

- [x] Implement the opt-in live suite described in [docs/TESTING.md](docs/TESTING.md), requiring an operator-prepared folder and fixture identities.
- [x] Document Linux/Bridge setup, public-certificate export and replacement, external credentials/configuration, VS Code build/debug workflow, and stdio client launch.
- [x] Document safe troubleshooting for unavailable Bridge, changed credentials/certificates, stale UIDs, limits, and unsupported content.
- [x] Record the operator-reported successful manual Bridge smoke test without mailbox data (2026-10-18).
- [ ] Record tested SDK/package/Bridge/Linux versions and full live acceptance outcomes without mailbox data.

Acceptance criteria:

- [ ] Real Bridge tests preserve persistent flags and message membership across repeated searches/reads, including unread messages and pre-existing deleted flags.
- [ ] Restart, connection loss, incorrect credentials, and changed certificates fail/recover as specified without fallback or mailbox mutation.
- [ ] A fresh setup can follow the documented workflow successfully, with secrets outside the repository and only public certificate material supplied to the application.
- [ ] The complete automated suite passes, and the live evidence is recorded. If live access is unavailable, leave this phase incomplete and explicitly record the limitation.

## Definition of done

- [ ] MVP complete: all four phases meet their acceptance criteria; documentation matches actual behavior; the intended Linux/Bridge combination has live evidence; no excluded feature or write capability is present. Read-only annotations alone, mocked adapter tests alone, or a successful build alone do not establish completion.

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

| 2026-10-17 | Phase 3 | Pinned MCP 2.2.0 XML/API documentation and official source at `6fa3825973949a9c4f0cd8af344e15a8db09dc35` reviewed; package and lock files unchanged | Three explicit `McpServerTool` instances; strict case-sensitive camelCase inputs, generated object input/output schemas, structured results plus equivalent JSON text, lowercase content statuses, fixed snake_case tool errors, and stderr-only operation/correlation/duration/outcome diagnostics |
| 2026-10-17 | Phase 3 | `DOTNET_CLI_HOME=/tmp/protonmcp-dotnet dotnet restore ProtonMailMcp.slnx --locked-mode --packages /tmp/protonmcp-packages -m:1` | Passed with NuGet audit access; initial sandbox attempt failed NU1900 because the audit service was unavailable. No audit suppression, version/lock changes, live credentials, or mailbox access |
| 2026-10-17 | Phase 3 | `DOTNET_CLI_HOME=/tmp/protonmcp-dotnet dotnet build ProtonMailMcp.slnx --configuration Release --no-restore` | Passed, zero warnings/errors, with runner IPC permission. Earlier single-node/shared-compilation-disabled builds also passed after schema construction fixes |
| 2026-10-17 | Phase 3 | Initial full offline suite with `--logger 'trx;LogFileName=phase3.trx' --results-directory /tmp/protonmcp-phase3-results -m:1` | 167 passed, 0 failed, 0 skipped (84 Unit, 40 Protocol, 43 Mcp); final run adds three strict numeric-string/case rejection cases. All gateway sessions use ephemeral synthetic fixtures |
| 2026-10-17 | Phase 3 | Final `DOTNET_CLI_HOME=/tmp/protonmcp-dotnet dotnet test ProtonMailMcp.slnx --configuration Release --no-build --filter 'Category!=LiveBridge' --logger 'trx;LogFileName=phase3-final.trx' --results-directory /tmp/protonmcp-phase3-results` | **170 passed, 0 failed, 0 skipped** (84 Unit, 40 Protocol, 46 Mcp), with runner IPC and synthetic loopback sockets permitted. Release build and locked restore passed; no live mailbox contacted |
| 2026-10-17 | Phase 3 | Contract, runtime-failure, cancellation, and captured-stdio process regressions through the official SDK client and real adapter | Exactly three tools discovered; optional filters, metadata/nulls, reference identity, all content statuses, body/attachment truncation, input rejection before connections, all ten error categories, TLS-before-auth rejection and fresh-connection recovery covered. Ordered scripts continue to admit only required read/session commands and require disconnect |
| 2026-10-17 | Phase 3 | Lifecycle regressions and limitations | Explicit SDK `notifications/cancelled` closes a partial-transfer session and permits a subsequent call within five seconds. High-level SDK local-token cancellation did not reliably deliver remote cancellation in this environment; a cancelled local await alone is not cleanup evidence. Stdin EOF drains a stalled LOGOUT under the real 30-second deadline, then exits cleanly. No safety boundary or production deadline changed |
| 2026-10-17 | Phase 3 | Regressions investigated; source/documentation review | Fixed nullable-root exported schemas rejected by SDK, required an explicit JSON metadata resolver, disabled web-default numeric-string coercion, and adjusted a synthetic MIME-type assertion to preserve adapter output casing. Fixture lifetime extended to 60 seconds solely to observe the production deadline. Standard sandbox test attempt aborted on VSTest socket permission before executing tests; runner IPC/synthetic loopback access was then granted |
| 2026-10-17 | Phase 3 | Markdown links/anchors/fences/JSON/newlines/whitespace across nine documents, C# whitespace/newlines, `git diff --check`, registration/mailbox source review | Passed. Usage/configuration/README/design/security/testing/development guides match implemented behavior and document SDK cancellation/EOF limits. No mailbox adapter changes, mutation path, assembly discovery, stdout diagnostic logging, new dependencies, or live access; Phase 4 and MVP completion remain pending |


| 2026-10-17 | Phase 4 | `dotnet --info`; `/etc/os-release`; existing project pins/lock files reviewed | Synthetic environment: Zorin OS 18.1 (Ubuntu 24.04 family), x64; SDK 10.0.112, runtime 10.0.12; MCP/Core 2.2.0, Hosting/PKCS 10.0.13, MailKit/MimeKit 4.18.1, xUnit 2.9.3, VS adapter 4.0.1, Test SDK 18.11.0 unchanged. Bridge version deliberately not discovered; no live compatibility claim |
| 2026-10-17 | Phase 4 | `DOTNET_CLI_HOME=/tmp/protonmcp-dotnet dotnet restore ProtonMailMcp.slnx --locked-mode --packages /tmp/protonmcp-packages -m:1`; `dotnet build ProtonMailMcp.slnx --configuration Release --no-restore -m:1 -p:UseSharedCompilation=false` with same CLI home | Restore passed, all projects up to date; final build passed, zero warnings/errors. Initial compile found incompatible empty collection for `IReadOnlySet`; corrected before final build. No package/lock changes. The documented normal `dotnet build ProtonMailMcp.slnx --configuration Release --no-restore` also passed with runner IPC permission |
| 2026-10-17 | Phase 4 | Focused `FullyQualifiedName~LiveAcceptanceSafetyTests\|FullyQualifiedName~LiveObserverTests` Release test filter | 17 passed, zero failed/skipped. External/symlink path rejection, invalid/valid known-fixture manifests, EXAMINE + UID FLAGS snapshot without content, Recent exclusion, persistent keywords, stale observer state disposal verified against synthetic fixtures. Initial full run: 177 passed, 1 failed, 0 skipped (broken-symlink test cleanup); corrected and focused run passed |
| 2026-10-17 | Phase 4 | `env -u PROTONMCP_LIVE DOTNET_CLI_HOME=/tmp/protonmcp-dotnet dotnet test tests/ProtonMailMcp.Tests/ProtonMailMcp.Tests.csproj --configuration Release --no-build --filter 'Category=LiveBridge'` | Zero passed/failed, one skipped: authorization absent, no settings read and no mailbox connection. This is gate evidence only; live acceptance **not run** |


| 2026-10-17 | Phase 4 | Ten Markdown documents checked for local links/anchors, fences, JSON examples, whitespace and final newlines; new C# whitespace/newlines; `git diff --check`; live code safety review | Passed. Added Linux/Bridge installation, operator public-certificate export/replacement, external configuration, VS Code tasks/debug/client launch and safe runtime troubleshooting. Official Proton/Microsoft docs reviewed; interactive setup remains unverified. No SMTP, mutable interface, non-PEEK body fetch, read/write selection, expunge or protocol logging introduced |

| 2026-10-17 | Phase 4 | Final `DOTNET_CLI_HOME=/tmp/protonmcp-dotnet dotnet test ProtonMailMcp.slnx --configuration Release --no-build --filter 'Category!=LiveBridge' --logger 'trx;LogFileName=phase4-final.trx' --results-directory /tmp/protonmcp-phase4-results` | **187 passed, 0 failed, 0 skipped**; 46 Mcp, 42 Protocol, 99 Unit. Duration 4m 3s. Initial sandbox invocation failed before tests on MSBuild IPC permissions; successful runs granted runner IPC and synthetic loopback sockets. Live category excluded; no live configuration or Bridge access |

| 2026-10-18 | Phase 4 — manual Bridge smoke test | Operator-performed command-line stdio test using external configuration; MCP initialization, discovery of exactly the three approved tools, `list_folders`, and `search_messages` with a delivery-date range and result limit | Successful, based on operator-provided evidence reviewed in this session. Folder listing and bounded delivery-date search executed against real Bridge; result truncation was reported. Later operator review identified imported messages with old sender dates and newer internal dates, limiting the date-search conclusion to execution of the former internal-date filter. This run does not validate the replacement sender-date filters. No raw output, mailbox data, identifiers, credentials, or personal paths retained in documentation. Bridge/Linux/runtime versions were not supplied for this run. No `get_message`, boundary-exclusion test, before/after flag or membership comparison, or failure/recovery test was demonstrated. The automated `LiveBridge` suite was not run; Phase 4 and MVP completion remain incomplete |

| 2026-10-18 | Documentation | Reviewed changes across seven Markdown documents; Python checks for local file links, balanced fences, final newlines and trailing whitespace; `git diff --check` | Passed. Recorded only the scope and outcome of the operator manual Bridge smoke test, with remaining acceptance gaps. Documentation only; no application tests rerun, live suite invoked, or mailbox accessed during this update |

| 2026-10-18 | Sender-date search | Reviewed pinned MailKit 4.18.1 XML docs for `SentSince`/`SentBefore`; `dotnet build ProtonMailMcp.slnx --configuration Release --no-restore -m:1 -p:UseSharedCompilation=false` | Passed, zero warnings/errors. Sender Date header queries replace delivery-date queries; `sentStart`/`sentEnd` replace received-date inputs. Dependencies/lock files and mailbox safety controls unchanged |
| 2026-10-18 | Sender-date search | `dotnet test ProtonMailMcp.slnx --configuration Release --no-build --filter 'Category!=LiveBridge' --logger 'trx;LogFileName=sender-date.trx' --results-directory /tmp/protonmcp-sender-date-results -m:1` | **193 passed, 0 failed, 0 skipped**, duration 4m 19s, with runner IPC and synthetic loopback sockets permitted. Initial non-escalated solution test command failed before tests on MSBuild IPC permissions. Exact sent-date protocol keys, one-sided/two-sided ranges with differing synthetic sender/internal dates, updated MCP schema, obsolete-argument rejection, malformed/equal/reversed ranges, and existing read-only regressions pass. No real mailbox accessed; live sender-date semantics remain unverified |
| 2026-10-18 | Sender-date documentation | Python checks of eight changed Markdown documents for local file links, fenced JSON, balanced fences, whitespace and final newlines; `git diff --check`; source diff review | Passed. Updated design, usage, test procedure and status summaries; qualified the earlier live search evidence after the imported-date discrepancy. Configuration reviewed, no settings changes required. No original mailbox output retained |

Phase 1 verification used `DOTNET_CLI_HOME=/tmp/protonmcp-dotnet` for writable tool state. The source/lock files use no machine-specific paths. Local config placement outside the checkout is an operator requirement, not runtime repository-root detection. Linux `statx` is required and fails closed if unavailable. Phase 1 proved local validation/stdio behavior only. Phase 2 now proves peer pin enforcement and adapter protocol safety against synthetic fixtures. Tool contracts have Phase 3 synthetic evidence; the 2026-10-18 operator smoke test adds limited live listing/search evidence. Full, version-specific Bridge compatibility and read-only state preservation remain unverified. MailKit metadata parsing and large matching UID sets are not promised constant-memory; transfer and returned-result bounds are enforced. The MVP is incomplete.

## Deferred work

HTTP/remote hosting, multi-account operation, attachment downloads, body search, pagination, indexing/caching, and background synchronization require a separate product decision. Sending or mailbox mutation is outside this product's read-only purpose, not a later MVP phase.

## Phase 4 handoff and outstanding operator work

The live suite is implemented in `tests/ProtonMailMcp.Tests/LiveBridge/LiveAcceptance.cs`, with a discovery-time skip and runtime authorization check. It requires command-scoped `PROTONMCP_LIVE=I_AUTHORIZE_READ_ONLY_BRIDGE_TESTS`, explicit external configuration/fixture paths, a dedicated single-folder allowlist excluding INBOX, and known identities/full expected membership and persistent flags. Reads and negative password/pin cases use the real adapter; a test-only read-only UID/FLAGS observer compares state to the operator baseline. Plain/HTML fixture output is verified by externally supplied hashes. The automated suite has not been run against Bridge. The separate operator-performed manual smoke test on 2026-10-18 used external configuration and successfully exercised discovery, listing, and date-range search. This documentation update did not access live settings, discover credentials, inspect Bridge stores, or connect to the mailbox.

Before full live acceptance, the operator must follow [Linux setup](docs/LINUX-SETUP.md) and [the exact fixture schema/stages](docs/TESTING.md#opt-in-real-bridge-acceptance): install/start Bridge, export/verify only its public certificate, prepare external credentials/configuration, populate the dedicated folder through a separate client, record all UIDVALIDITY/UID/flag/keyword identities and expected text hashes, pause other writers, and authorize the specific run. The suite never prepares, modifies, or cleans up messages. A pre-existing deleted flag is required for full acceptance; `unsupported` must remain a recorded limitation.

Still unverified: version-specific Bridge/Linux compatibility, live message-body retrieval, replacement sender-date matching and live date-boundary exclusion, persistent live flags/membership, stopped-service and restart stages, an operator-controlled interruption during an active read, actual certificate replacement, and a fresh VS Code/client setup. Wrong-pin rejection is an automated live scenario, not proof of an actual certificate rotation. The interruption and certificate-replacement procedures are documented operator checks; the suite does not control Bridge. All Phase 4 acceptance criteria and MVP completion remain unchecked. Application behavior, dependencies and lock files are unchanged by Phase 4.
