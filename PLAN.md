# Implementation status and acceptance

## Contents

- [Current state](#current-state)
- [Progress](#progress)
- [Search pagination](#search-pagination)
- [Attachment downloads](#attachment-downloads)
- [Phase 4 — Bridge validation and handoff](#phase-4--bridge-validation-and-handoff)
- [Evidence ledger](#evidence-ledger)
- [Documentation consolidation — 2026-10-24](#documentation-consolidation--2026-10-24)
- [ChatGPT/Codex setup documentation — 2026-10-25](#chatgptcodex-setup-documentation--2026-10-25)
- [Independent review — 2026-10-30](#independent-review--2026-10-30)
- [Definition of done](#definition-of-done)
- [Deferred work and known limits](#deferred-work-and-known-limits)

## Current state

The .NET 10 server, read-only MailKit adapter, three base MCP tools, sender-date search, folder policies, search pagination and optional attachment downloads are implemented and synthetically tested. Phase 4 tooling and handoff guides are available; **full live acceptance and MVP completion remain unverified**.

Current authorization: the 2026-10-30 independent review, focused correctness/security fixes with regression tests, locked restore, Release build, complete offline tests, local publish and published-artifact protocol verification. This does not authorize plugin installation, HTTP hosting, or live mailbox access. The previous setup-guide cleanup remains recorded below. Existing feature authorizations are recorded below and in the [history](docs/archive/IMPLEMENTATION-HISTORY.md). A plan or pending checkbox does not authorize a new feature or live mailbox access. Live work requires explicit operator authorization and externally prepared configuration/fixtures under [TESTING.md](docs/TESTING.md#opt-in-real-bridge-acceptance).

Read [AGENTS.md](AGENTS.md), [DESIGN.md](DESIGN.md), [SECURITY.md](SECURITY.md), and the [development workflow](docs/DEVELOPMENT.md) before implementation. Routine work within an authorized scope needs no repeated approval; changes to safety boundaries require a product decision.

## Progress

| Work | Authorization | Implemented and verified | Remaining acceptance |
| --- | --- | --- | --- |
| Phase 1: foundation | Design approved 2026-10-17 | Pinned SDK/packages and tracked locks, configuration validation, stdio host and synthetic tests | Complete for this phase |
| Phase 2: mailbox adapter | Approved phase scope; historical checklist linked below | Serialized connections, TLS pinning, UID identity, EXAMINE/PEEK, body limits and failure cleanup | Synthetic phase complete; Bridge evidence belongs to Phase 4 |
| Phase 3: MCP interface | Approved phase scope; historical checklist linked below | Three explicit base tools, schemas/results/errors, SDK process tests and stdout hygiene | Synthetic phase complete |
| Sender-date search | 2026-10-18 | `sentStart`/`sentEnd`, SENTSINCE/SENTBEFORE, obsolete-input rejection | Live date matching and boundaries |
| Folder access policies | 2026-10-19 | Allowlist/AllExcept, per-call policy checks, namespace discovery and output bounds | Live AllExcept discovery/label behavior |
| Search pagination | Design and implementation 2026-10-21 | Authenticated cursors, bounded summary pages, race/error/MCP regressions | Live traversal and state preservation |
| Attachment downloads | Design and implementation 2026-10-21 | Private storage, bounded strict decoding, conditional fourth tool, filesystem/protocol/MCP regressions | Intended-client PDF interpretation and live downloads |
| Phase 4: Bridge validation and handoff | Tooling implemented; live runs require separate opt-in | Gated suite, setup, launch, debugging and troubleshooting guides | Criteria below |

Completed deliverables, original acceptance checklists and implementation sequences remain in the [implementation history](docs/archive/IMPLEMENTATION-HISTORY.md). Current behavior is defined by DESIGN, SECURITY and the linked feature contracts.

## Search pagination

The [contract and acceptance matrix](docs/SEARCH-PAGINATION.md) define cursor binding, descending UID traversal, race behavior and limits. Synthetic verification completed with 290 passing tests at implementation time; subsequent full-suite evidence is below.

- [x] Stable traversal, query/token validation, UIDVALIDITY, candidate-based progress, real-adapter safety, MCP compatibility and documentation verified synthetically.
- [ ] Verify live pagination membership and persistent flags for recorded Bridge/Linux versions under explicit opt-in.

## Attachment downloads

The [contract and acceptance matrix](docs/ATTACHMENT-DOWNLOADS.md) define private storage, byte/transfer quotas, retention, errors and publication semantics.

- [x] Disabled/enabled compatibility, eligible references, byte/hash integrity, private storage, transfer limits, cleanup and real-adapter/MCP behavior verified synthetically.
- [x] Locked restore, Release build, complete synthetic suite and documentation checks recorded.
- [ ] Verify that the intended client reads a synthetic downloaded PDF using the returned path; record client version and filesystem/sandbox limitations. SDK byte transport alone does not establish PDF interpretation.
- [ ] Verify explicitly authorized live attachment hashes and persistent flags/membership for recorded Bridge/Linux versions.

## Phase 4 — Bridge validation and handoff

- [x] Implement gated live suite, Linux/Bridge setup, certificate replacement, external configuration, VS Code debugging/launch and troubleshooting guides.
- [x] Record the limited operator-reported 2026-10-18 smoke test below.
- [ ] Record tested SDK/package/Bridge/Linux versions and full live acceptance outcomes without mailbox data.
- [ ] Preserve persistent flags and membership across repeated searches/reads, including unread messages and pre-existing deleted flags. Record unsupported deleted state as a limitation, not acceptance.
- [ ] Verify replacement sender-date matching, inclusive/exclusive and one-sided boundaries, and differing sender/internal dates where available.
- [ ] Verify message-body retrieval and plain/HTML conversion against independently known text hashes.
- [ ] Verify stopped-service/restart recovery, incorrect credentials, wrong pins, an operator-controlled interruption during an active read, and actual certificate replacement without fallback or mutation. Wrong-pin rejection alone does not establish certificate-rotation behavior.
- [ ] Verify AllExcept discovery/label behavior separately; the existing live suite remains restricted to one dedicated allowlisted folder.
- [ ] Verify a fresh Linux/VS Code/client setup with secrets outside the repository.
- [ ] Run the complete automated suite and record live evidence; leave Phase 4 incomplete if live access is unavailable.

Follow [Linux setup](docs/LINUX-SETUP.md) and the [exact fixture schema/stages](docs/TESTING.md#opt-in-real-bridge-acceptance). The operator prepares fixtures and pauses other writers through a separate client. This server and its suite never prepare, modify or clean up live mail.

## Evidence ledger

| Date | Verification | Result / limitation |
| --- | --- | --- |
| 2026-10-21 | Latest runtime verification: SDK 10.0.113; locked restore, Release build and complete non-live suite | Passed: zero build warnings/errors; **379 passed, 0 failed, 0 skipped**, 5m 34s. Network/IPC permissions were needed for successful retries; audit controls remained enabled. Pins, commands, earlier failures and detailed outcomes are preserved in the [historical ledger](docs/archive/IMPLEMENTATION-HISTORY.md#evidence-ledger). These results were not rerun for documentation consolidation. |
| 2026-10-18 | Operator-performed real Bridge smoke test | Initialization, three-tool discovery, folder listing and bounded delivery-date search executed successfully. Imported messages later showed differing sender/internal dates, so this does not verify the replacement sender-date filters. Versions were not supplied. No body retrieval, date-boundary exclusion, before/after flags/membership or failure/recovery was demonstrated. No mailbox values are retained; the automated live suite has not been run. |
| 2026-10-24 | Documentation consolidation: `python3 /tmp/check_proton_docs.py`; `git diff --check` | Passed: local links/anchors, JSON examples, fences/formatting, configuration example, SDK pin, interface and command paths. Historical plan/evidence and attachment sequence preserved; exact live procedures/gates retained apart from the canonical build-command link. Documentation only: build/application/live tests not rerun; no mailbox accessed. |
| 2026-10-25 | ChatGPT/Codex setup docs: official OpenAI MCP, plugin packaging, desktop-only and custom-server pages reviewed; `python3 /tmp/check_proton_client_docs.py`; `git diff --check` | Documentation checks passed for 5 Markdown files, 76 existing local links/anchors, 9 JSON blocks, 1 TOML block, plugin identity/path and application launch/transport consistency. Initial link check exposed the pre-existing missing `docs/archive/IMPLEMENTATION-HISTORY.md`; the final check explicitly reports/exempts that target. Client examples are not interactively verified or schema-validator tested. No installation, application tests, live access or HTTP changes performed. |
| 2026-10-25 | Setup-guide cleanup: `python3 /tmp/check_proton_setup_guide.py`; `git diff --check -- docs/CHATGPT-CODEX.md PLAN.md` | Passed: 5 JSON examples, 15 local links/anchors, matching plugin identities/versions and portable/compatibility launch arguments. Removed troubleshooting procedures, failed mitigations and investigation history from the guide; retained the final four-file setup, installation/refresh, tool verification and acceptance limits. Historical evidence remains below. Documentation only; no client settings changed, application/live tests rerun or mailbox accessed. |

2026-10-30 independent review: `dotnet restore ProtonMailMcp.slnx --locked-mode`; `dotnet build ProtonMailMcp.slnx --configuration Release --no-restore`; `dotnet test ProtonMailMcp.slnx --configuration Release --no-build --filter 'Category!=LiveBridge'`; `dotnet publish src/ProtonMailMcp/ProtonMailMcp.csproj --configuration Release --no-build --no-restore --output /tmp/protonmcp-review-publish`; `PROTONMCP_TEST_APPLICATION=/tmp/protonmcp-review-publish/ProtonMailMcp.dll dotnet test ProtonMailMcp.slnx --configuration Release --no-build --filter 'Category=Mcp'`; `dotnet list ProtonMailMcp.slnx package --vulnerable --include-transitive --no-restore`; `python3 /tmp/check_proton_review_docs.py`; `git diff --check`. Restore/build/publish passed; zero build warnings/errors; final offline **398 passed**, published MCP **69 passed**, each 5m 35s with zero failures/skips. NuGet reported no known vulnerable packages. Documentation: 16 files, 278 local links/anchors, 21 JSON blocks. Recovered the missing archive from commit `fd478e5`, verified its historical-content equality and corrected three PLAN navigation anchors. Initial restricted restore failed without diagnostics; authorized retries used package/cache and synthetic IPC/loopback access without disabling audit. Three defects were reproduced and fixed; exact evidence, prior-run caveats and residual risks are in [STATUS.md](STATUS.md). No live settings, credentials or mailbox accessed; live/client acceptance and MVP completion remain unverified.

Add new verification here with actual commands, outcomes and limitations. Preserve older evidence in the linked archive; never report skipped live tests as passing acceptance.

2026-10-25 client troubleshooting (operator requested): `codex plugin list --marketplace Private --json` confirmed the plugin installed/enabled. `/tmp/proton-discovery-check.py` sent only MCP initialization and `tools/list`. The sandbox caused `configuration_path_unsafe` because system directory ownership was remapped; an unsandboxed retry returned `configuration_download_directory_in_use`. Process metadata identified an older VS Code Codex-launched server. After terminating only that identified server, `python3 /tmp/proton-discovery-check.py` outside the sandbox succeeded with all four tools using the unchanged operator configuration, then exited. A separate temporary private configuration omitting downloads also discovered the three base tools and was removed. No mailbox operations, attachment downloads, or application changes occurred. ChatGPT plugin invocation and live acceptance remain unverified. Client runtimes observed: VS Code CLI 0.162.0-alpha.17.2; desktop CLI 0.164.0-alpha.11.1. `git diff --check` passed; application tests were not rerun for this documentation-only troubleshooting note.

2026-10-25 follow-up plugin troubleshooting: the earlier standalone-server check did not establish ChatGPT plugin loading. Diagnostic `plugin/read` through `/usr/lib/chatgpt/resources/codex app-server --stdio` recognized the installed plugin and its `protonmail` MCP declaration. `mcpServerStatus/list` returned no Proton Mail tools while the old direct `[mcp_servers.protonmail]` table remained with `enabled = false`. Removed only that obsolete direct table from the operator's client configuration, preserving a private backup and checking that all other parsed TOML settings were unchanged. Repeating the local-runtime inventory immediately returned `list_folders`, `search_messages`, `get_message`, and `download_attachment`. This establishes observed same-name masking on desktop runtime 0.164.0-alpha.11.1; previous advice that disabling the direct entry was sufficient is corrected in the client guide. No mailbox tools were invoked. Client restart/chat invocation and live acceptance remain pending. `git diff --check -- docs/CHATGPT-CODEX.md PLAN.md` passed; no application tests were rerun.

2026-10-25 operator-requested log investigation: filtered current desktop logs and queried only MCP lifecycle targets/fixed startup codes in the runtime log database, suppressing conversation payloads and identifiers. The affected Work turn at 09:36:36 UTC omitted `protonmail` because no exact ready client was bound; earlier 09:34:12 UTC handshake failures corresponded to `configuration_download_directory_in_use`. Optional resource-list warnings were also present. Desktop logs show a separate connected Local Work executor; they do not establish why that turn lacked the client. `python3 /tmp/proton-local-session-discovery.py` outside the sandbox used `thread/start` with `ephemeral: true` and `mcp_optional_startup_grace_ms: 0`, followed by thread-scoped `mcpServerStatus/list`: all four plugin tools were discovered. No model prompt or mailbox tools were sent. Work invocation remains unresolved; previous inventory fixes must not be reported as end-to-end success. No further saved client settings or application behavior changed. `git diff --check -- docs/CHATGPT-CODEX.md PLAN.md` passed; application/live tests not rerun.

2026-10-25 readiness mitigation: verified OpenAI's documented optional-server startup grace, then added only top-level `mcp_optional_startup_grace_ms = 0` to the operator's client TOML, preserving a private backup and asserting all other parsed settings identical. `python3 /tmp/proton-saved-config-session.py` outside the sandbox started an ephemeral local session without per-session startup overrides and discovered all four plugin tools. No model prompt, mailbox call, download, or server code change occurred. This is a documented timing mitigation, not a proven cause/fix for the affected Work path. Operator was asked to retry tool-name discovery in a restarted Work chat; outcome remains pending. `git diff --check -- docs/CHATGPT-CODEX.md PLAN.md` passed; no application/live tests rerun.

2026-10-25 failed readiness retry and compatibility experiment: operator reported the same Work error with startup grace zero and a transient Local Work popup. Latest desktop logs show an `ENOENT` copying bundled `codex-app-tools/server.mjs`, followed by successful retry/connection; the later affected turn still omitted `protonmail` without an exact ready client. No cause between those events is established. Removed only the ineffective startup-wait override, preserving other parsed settings. Added supported compatibility `.codex-plugin/plugin.json` plus `.mcp.json` alongside the operator's portable plugin, with identical launch arguments; `/usr/lib/chatgpt/resources/codex plugin add protonmail@Private --json` refreshed installation. `python3 /tmp/proton-verify-compatibility.py` verified source/installed file equality and launch equivalence; `python3 /tmp/proton-installed-metadata.py` confirmed installed/enabled handoff metadata. This is an unproven compatibility experiment; the requested Work restart/retry remains pending. No ChatGPT application files, server code or mailbox data changed. `git diff --check -- docs/CHATGPT-CODEX.md PLAN.md` passed; application/live tests not rerun.

2026-10-25 operator-confirmed resolution: after installing the added compatibility manifests and restarting ChatGPT, the operator reported that the Proton Mail tools were listed in the local Work chat. Mark installation and `@` tool exposure verified for this client; preserve the earlier failed attempts as evidence. Updated the setup recipe to include the working compatibility files and removed blanket claims that interactive discovery remains unverified. No mailbox calls or intended-client attachment/file interpretation were demonstrated; live acceptance and MVP completion remain pending. `git diff --check -- docs/CHATGPT-CODEX.md PLAN.md` and fenced JSON/TOML parsing passed; application/live tests not rerun.

## Documentation consolidation — 2026-10-24


- [x] Consolidate status/history, clarify document responsibilities, remove completed backlog descriptions and validate documentation. Active documentation is about 38% shorter; historical evidence remains in the archive.

## ChatGPT/Codex setup documentation — 2026-10-25

- [x] Add [client setup instructions](docs/CHATGPT-CODEX.md) with official sources, direct local MCP registration, personal plugin/marketplace examples for `@`, hosted-web limitations, and navigation links.
- [x] Validate example syntax, launch/transport consistency and local links, with the missing historical archive reported above.
- [x] Operator confirmed interactive installation and local Work `@` tool exposure after adding compatibility manifests on 2026-10-25.
- [x] Clean up the client guide to show the final working setup without troubleshooting steps or investigation history.
- [ ] Verify intended-client filesystem access and actual mailbox operations; live acceptance still requires explicit operator opt-in.

## Independent review — 2026-10-30

The current review and command evidence are recorded in [STATUS.md](STATUS.md). This is the Proton Mail stdio/IMAP server: no OpenAPI specification, HTTP routing/origin/redirect handling, OAuth refresh flow or financial model exists in the checkout.

- [x] Independently review implementation, contracts, pinned dependencies, tests and documentation; reproduce three correctness/security defects before fixes.
- [x] Reject unexpected/duplicate/partial text literals; reuse strict transfer decoding for text; prevent authentication mechanism/LOGIN fallback after a failed credential attempt.
- [x] Add real-adapter and MIME regressions, published-DLL test selection and explicit child-process exit waits.
- [x] Recover the missing historical archive from the pre-consolidation Git version, with provenance and corrected relative links.
- [x] Final Release build passed with zero warnings/errors; complete offline suite: **398 passed, 0 failed/skipped**; published-DLL MCP suite: **69 passed, 0 failed/skipped**. Locked restore, local publish, dependency audit and documentation checks passed; exact commands and limits are in STATUS.
- [ ] Authorized live Bridge acceptance and intended-client PDF interpretation remain unverified; MVP/release completion remains unchecked.

## Definition of done

- [ ] MVP complete: all phases meet their acceptance criteria; documentation matches behavior; the intended Linux/Bridge combination has live evidence; no excluded feature or mailbox write capability is present. A build, annotations or mocked adapter tests alone do not establish completion.

## Deferred work and known limits

[BACKLOG.md](BACKLOG.md) tracks deferred proposals; inclusion does not authorize implementation. HTTP hosting, multiple accounts, body search, persistent indexing/caching, background sync and direct Proton APIs require a separate scope decision. Sending or mailbox mutation is outside this product's read-only purpose.

Search UID sets and MailKit metadata parsing are not constant-memory, pagination is not a snapshot, and label exclusions restrict access paths rather than overlapping messages. Downloads retain private plaintext until operator cleanup; intended-client path access is a separate acceptance requirement. See [SECURITY.md](SECURITY.md) for trust boundaries and the [archived security review](docs/archive/IMPLEMENTATION-HISTORY.md#security-and-privacy-review--2026-10-18) for the resolved configuration-integrity finding and its evidence.
