# Implementation status and acceptance

## Current state

The .NET 10 server, read-only MailKit adapter, three base MCP tools, sender-date search, folder policies, search pagination and optional attachment downloads are implemented and synthetically tested. Phase 4 tooling and handoff guides are available; **full live acceptance and MVP completion remain unverified**.

Current authorization: consolidate documentation as requested on 2026-10-24. Existing feature authorizations are recorded below and in the [history](docs/archive/IMPLEMENTATION-HISTORY.md). A plan or pending checkbox does not authorize a new feature or live mailbox access. Live work requires explicit operator authorization and externally prepared configuration/fixtures under [TESTING.md](docs/TESTING.md#opt-in-real-bridge-acceptance).

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

Add new verification here with actual commands, outcomes and limitations. Preserve older evidence in the linked archive; never report skipped live tests as passing acceptance.

## Documentation consolidation — 2026-10-24

- [x] Consolidate status/history, clarify document responsibilities, remove completed backlog descriptions and validate documentation. Active documentation is about 38% shorter; historical evidence remains in the archive.

## Definition of done

- [ ] MVP complete: all phases meet their acceptance criteria; documentation matches behavior; the intended Linux/Bridge combination has live evidence; no excluded feature or mailbox write capability is present. A build, annotations or mocked adapter tests alone do not establish completion.

## Deferred work and known limits

[BACKLOG.md](BACKLOG.md) tracks deferred proposals; inclusion does not authorize implementation. HTTP hosting, multiple accounts, body search, persistent indexing/caching, background sync and direct Proton APIs require a separate scope decision. Sending or mailbox mutation is outside this product's read-only purpose.

Search UID sets and MailKit metadata parsing are not constant-memory, pagination is not a snapshot, and label exclusions restrict access paths rather than overlapping messages. Downloads retain private plaintext until operator cleanup; intended-client path access is a separate acceptance requirement. See [SECURITY.md](SECURITY.md) for trust boundaries and the [archived security review](docs/archive/IMPLEMENTATION-HISTORY.md#security-and-privacy-review--2026-10-18) for the resolved configuration-integrity finding and its evidence.
