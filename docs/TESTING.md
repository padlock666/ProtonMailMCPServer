# Test strategy

Use one xUnit project with `Unit`, `Protocol`, `Mcp` and gated `LiveBridge` categories. Standard tests use synthetic data and require no Proton account, personal credentials or running Bridge. [SECURITY.md](../SECURITY.md) defines mandatory controls; [PLAN.md](../PLAN.md) records evidence and pending live acceptance.

## Coverage and limitations

Test observable behavior; mocks of `IReadOnlyMailbox` are suitable for tool mapping only and cannot prove read-only adapter behavior.

| Area | Coverage and fixtures |
| --- | --- |
| Configuration and policy (`Unit`, `Protocol`, `Mcp`) | Strict fields/types, both folder modes and legacy compatibility, INBOX/exact-name rules, denied direct calls, namespace/unsubscribed/new-folder discovery, duplicate/nonselectable/missing folders and listing overflow/recovery |
| Protected files/storage (`Unit`) | Open-handle ownership/modes, private parents, trusted ancestors, symlinks, FIFOs/directories, replacement races, certificate format/dates, safe diagnostics; download locks, quotas, hard links, no-overwrite publication, cleanup and strict decoder boundaries |
| Search and identity (`Unit`, `Protocol`, `Mcp`) | AND filters, To/Cc, unread tri-state, sender-date boundaries and differing internal dates, UID ordering, default/max limits, missing UIDs and stale UIDVALIDITY |
| Pagination (`Unit`, `Protocol`, `Mcp`) | `SearchCursorTests` and `SearchPaginationTests`: binding/tampering/restart, chains above 50 matches, empty/exact/overflow pages, sparse/extreme UIDs, vanished candidates, arrivals/unread changes, finite ranges, policy/epoch errors and recovery; see the [acceptance matrix](SEARCH-PAGINATION.md#acceptance-matrix) |
| MIME/body (`Unit`, `Protocol`) | Plain/HTML and alternative/related selection; attached text/messages excluded; strict transfer/charset decoding, missing/malformed/unsupported bodies, advertised/actual literal caps, metadata/control/surrogate bounds and output truncation; inert links/images |
| Downloads (`Unit`, `Protocol`, `Mcp`) | `DownloadStorageTests` and `AttachmentDownloadTests`: top-level/nested parts, eligibility and IDs, restart/repeats, byte/hash/path handoff, malformed encodings, exact/overflow limits, unsolicited UID/section/offset/duplicate literals and missing payloads; retain body-only attachment exclusion; see the [acceptance matrix](ATTACHMENT-DOWNLOADS.md#acceptance-matrix) |
| MCP process (`Mcp`) | Official SDK child-process initialization/discovery, exactly three/four tools, strict schemas/annotations, structured/text result equivalence, content statuses, input rejection before I/O, fixed runtime errors, cancellation/recovery, protocol-only stdout and sanitized stderr |
| Concurrency and failure (`Protocol`, `Mcp`) | Queued/in-flight cancellation, production deadline, stalled logout, malformed/rejected commands, broken sockets, gate release and a fresh successful connection; EOF/shutdown behavior below |

## Protocol and process verification

`Fixtures/ScriptedImapServer.cs` runs the actual `MailKitMailbox` on ephemeral loopback ports with generated test-only TLS certificates and synthetic settings/messages. Ordered scripts admit only expected commands/connections and require socket closure. Never reuse Bridge's listener or weaken production TLS for tests.

Require pin validation before authentication in both TLS modes, EXAMINE with confirmed read-only access, UID addressing, BODYSTRUCTURE before selected-part PEEK, and safe logout/disposal without CLOSE, expunge or mutation. Verify these on success, rejection, stale/missing references, oversized responses, cancellation and disconnect. No full-message fetch is allowed. Capture synthetic traffic for assertions; never enable production protocol logging.

Launch the built application through the pinned official SDK client, keeping build banners out of stdout. Capture stdout/stderr separately; parse stdout as MCP JSON and require diagnostics to contain only operation, correlation ID, duration and fixed outcome, excluding sentinel secrets and sensitive fixture fields. Check schema behavior rather than snapshotting entire generated descriptions. Tests cover default initialization and handshake revision `2025-11-25`.

## Environment and evidence limits

Linux is required, including `statx` and `mkfifo` for special-file fixtures. Restricted runners may need MSBuild/compiler/VSTest IPC and synthetic loopback permission; this does not authorize Bridge access. Synthetic files/certificates are temporary. Foreign-owner integration needs privileges and is not exercised. Native disk-full/flush fault injection, forced process-kill timing, privileged ownership cases and network-filesystem compatibility are not claimed as tested; supported download storage is local 64-bit Linux.

The fixture lifetime is 60 seconds so tests can observe the unchanged production 30-second deadline. A controlled clock checks that the deadline starts before queue acquisition and that queued timeout cannot release another operation's gate. No production deadline override or test-only host flag is exposed.

SDK byte/path handoff establishes local transport, not PDF interpretation by the intended client. Synthetic protocol tests establish pinned MailKit behavior, not live Bridge compatibility or persistent flag preservation. MailKit metadata parsing and matching-UID materialization remain outside constant-memory guarantees.

## Cancellation and shutdown

The SDK regression sends explicit `notifications/cancelled` for a known request ID using `McpClient.SendMessageAsync`, awaits cancellation and requires a successful subsequent call within five seconds. Cancelling a high-level SDK call's local token did not reliably deliver remote cancellation in this environment; a cancelled local await is not cleanup evidence.

Raw stdio tests exercise complete results and stdin EOF during stalled LOGOUT for reads/downloads. EOF drains the active operation under its existing 30-second deadline, then exits; immediate cancellation is not promised. Ordinary download cleanup leaves no completed/temporary file before publication. Process termination closes sockets; no CLOSE/expunge is admitted. Crash leftovers and publication/response-loss semantics remain as defined in the download contract.

## Opt-in real Bridge acceptance

These tests require deliberate operator setup and are excluded from the normal automated run. Use an external configuration file, a narrow dedicated folder allowlist, and operator-provided known fixture identities. Merely detecting Bridge or finding credentials is never an opt-in signal.

The operator prepares messages through a separate mail client before the test: at least one unread message, one read message, a text/HTML pair, a message with an attachment, and a message already carrying a deleted flag if Bridge supports that state. This server and suite never create, send, change, move, or delete fixtures. If a required state cannot be represented, record the limitation rather than manufacture evidence.

Procedure:

1. Stop other clients/background rules from changing the test folder and wait for Bridge synchronization to settle.
2. Using read-only access, capture UIDVALIDITY, message UID membership/count, and persistent flags for the dedicated folder. Do not compare session-only `Recent` state as a persistent flag.
3. Repeat folder listing, filtered/unfiltered searches, and reads of the known unread/read messages. Check sender-date ranges against independently known fixture Date headers, including inclusive start, exclusive end, one-sided ranges, and dates differing from internal dates where available. Exercise text/HTML conversion and confirm attachment metadata without payload retrieval.
4. Capture the same state again. Require identical UIDVALIDITY, membership/count, and persistent flags; specifically require unread messages to remain unread and deleted-flagged messages to remain present.
5. Exercise Bridge stop/restart, wrong credentials, and a wrong pin using isolated test settings. Never automatically replace the configured trusted pin. Resume correct settings and verify a new call works.
6. Compare state again after recovery. An external change invalidates the comparison; report/repeat under controlled conditions rather than blaming or hiding it.
7. Record tested Linux, .NET SDK, SDK/package, and Bridge versions, test names, date, and outcome. Keep fixture identities, addresses, subjects, credentials, and real transcripts out of the evidence ledger and chat.

No automatic fixture cleanup is permitted. The operator manages the test mailbox separately after the test session. If live configuration is unavailable, mark live acceptance not run and leave Phase 4 incomplete.

## Commands and categorization

The xUnit project uses a `Category` trait: `Unit`, `Protocol`, `Mcp`, and `LiveBridge`. Every test must have one category, with a discovery check to prevent unclassified tests bypassing the live exclusion. Live tests also require an explicit opt-in flag and external configuration; selecting the category alone must not discover credentials or contact a mailbox.

Use the canonical [build and synthetic test commands](DEVELOPMENT.md#build-and-test-workflow) from the repository root.

The live test is `LiveAcceptance.OperatorPreparedFolderPreservesState`. Category selection without authorization skips it before settings are loaded. Every invocation also checks authorization at runtime. No endpoint/credential discovery occurs. Do not run it until the operator supplies configuration and explicitly authorizes access.

Prepare these **external** files in a private directory outside the checkout (symlink paths resolving into the checkout are rejected):

- A normal application JSON configuration in allowlist mode (legacy or explicit), with exactly one allowed dedicated folder, excluding INBOX. AllExcept mode is rejected by the live suite. Its password and public-certificate paths must also be external. Existing production secret/TLS validation applies unchanged.
- A fixture manifest, at most 65,536 bytes, with the case-sensitive schema below. It describes **every** message in the folder (2–50 messages); no automatically adopted baseline is written. Known identifiers must be supplied by the operator through read-only inspection in a separate trusted client. Do not obtain them by inspecting Bridge stores. The suite compares this baseline before and after reads and after failure/recovery stages.

```json
{
  "Folder": "<dedicated-Bridge-full-folder-name>",
  "UidValidity": 7,
  "Messages": [
    { "Uid": 41, "Flags": 0, "Keywords": [] },
    { "Uid": 42, "Flags": 1, "Keywords": [] },
    { "Uid": 43, "Flags": 8, "Keywords": [] }
  ],
  "DeletedState": "present",
  "PlainUid": 41,
  "HtmlUid": 42,
  "AttachmentUid": 43,
  "PlainTextSha256": "<64-hex-digits>",
  "HtmlTextSha256": "<64-hex-digits>"
}
```

All numbers above are synthetic placeholders. Flags use MailKit persistent bits: Seen=1, Answered=2, Flagged=4, Deleted=8, Draft=16; sum combined bits. Keywords are the exact persistent keyword strings. `Recent` is ignored, and the keyword marker itself is excluded from the bit comparison. At least one fixture must be unread and one read. Plain and HTML UIDs must differ; the attachment may share another role. Use a plain-only primary body and an HTML-only primary body (no plain alternative) so both conversion paths are exercised. SHA-256 values describe the UTF-8 encoding of the exact expected returned text, including whitespace/newlines; prepare these from known fixture text independently, not from the first server result. Keep bodies small and below the output cap. The attachment fixture must have metadata; payload exclusion has synthetic wire evidence rather than live protocol capture.

If Bridge cannot represent a pre-existing deleted flag, set `DeletedState` to `unsupported`, omit that flag from all fixtures, and record the limitation. Such a run cannot satisfy the deleted-flag acceptance criterion. Never modify fixtures during the run to manufacture a state. Keep the manifest private: identifiers, keywords, and hashes belong outside version control and chat.

Only after explicit authorization, run from the repository root with locally substituted external paths:

```bash
PROTONMCP_LIVE=I_AUTHORIZE_READ_ONLY_BRIDGE_TESTS \
PROTONMCP_LIVE_CONFIG=/absolute/private/directory/live-config.json \
PROTONMCP_LIVE_FIXTURES=/absolute/private/directory/live-fixtures.json \
PROTONMCP_LIVE_STAGE=reads \
dotnet test ProtonMailMcp.slnx --configuration Release --no-build --filter 'Category=LiveBridge'
```

`reads` compares full UID membership/count, UIDVALIDITY, persistent flags and keywords; repeats discovery, unfiltered/unread/read searches and all known fixture reads twice; verifies text hashes and attachment metadata; then tests a random incorrect password and a synthetic wrong pin in isolated in-memory settings. Each search also follows cursors at page size 1 and compares the complete ordered UID chain against the known manifest. It validates the truncation/cursor invariant, folder epoch, per-page count, and finite traversal without recording cursor values. The existing 2–50 fixture-message limit is unchanged; no fixture preparation or mutation is added. It requires the expected sanitized failures and a fresh successful call with correct settings, followed by another state comparison. These negative tests cause one failed authentication attempt; include that in operator authorization. No settings files are changed. The observer uses only read-only selection and UID/FLAGS metadata, with a 30-second per-session deadline; the whole test has a five-minute deadline. Errors intentionally omit live values and inner exceptions. This pagination extension has not been run against real Bridge.

For a controlled stop/restart, keep the same manifest, stop Bridge yourself, and invoke the same command with `PROTONMCP_LIVE_STAGE=unavailable`. It requires `bridge_unavailable` without retry. Restart Bridge yourself, wait for sync, and invoke with `PROTONMCP_LIVE_STAGE=recovered`; this repeats state/read checks against the original manifest. A stopped-service check is not evidence of an interrupted active transfer.

Separately, using a trusted MCP client and the same dedicated configuration, deliberately stop Bridge during an active read. Require a sanitized `bridge_unavailable` or `timeout`, no retry or fallback, then restart Bridge and require a fresh successful call and a `recovered` stage against the unchanged manifest. Record this manual connection-loss result separately; the suite never controls Bridge or pauses to solicit operator actions. Do not capture live protocol traces. If the interruption missed the active operation, record it as unverified and repeat only with authorization.

A wrong-pin test proves mismatch rejection; actual certificate rotation remains an operator workflow. If rotating Bridge's certificate deliberately, retain the old public pin, require `tls_failed`, independently export/verify the replacement, restart the MCP host with the new external pin, and repeat `recovered`. Never rotate credentials/certificates or repair/reset Bridge automatically. If UIDVALIDITY changes, the old baseline is stale: stop, investigate, and explicitly re-prepare a baseline; do not silently accept new identities.

Keep opt-in variables scoped to a single command. Do not export them globally or place them in test/build tasks. Ordinary tests explicitly exclude `LiveBridge` even when opt-in exists. Record a skipped live test as **not run**, never as passing live acceptance.

For dependency changes, update the lock file intentionally before validating locked restore. For MailKit changes rerun protocol tests; for Bridge compatibility rerun live acceptance. Record actual results and any skipped tests in [PLAN.md](../PLAN.md).


## Attachment-download live stage

The optional live `downloads` stage requires **both** `PROTONMCP_LIVE=I_AUTHORIZE_READ_ONLY_BRIDGE_TESTS` and `PROTONMCP_LIVE_DOWNLOADS=I_AUTHORIZE_LOCAL_ATTACHMENT_FILES`, plus `PROTONMCP_LIVE_STAGE=downloads`, the existing external settings/manifest paths, and an operator-prepared private `AttachmentDownloads.Directory`. Authorization is checked before loading settings. The directory must be outside the checkout; existing dedicated-folder/known-membership rules remain mandatory. Add these fields to the external fixture manifest, using independently known values:

```json
{
  "DownloadAttachmentId": "2",
  "DownloadSha256": "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
  "DownloadSizeBytes": 1000
}
```

These are synthetic placeholders and a fragment, not a complete live manifest. The download uses the manifest's existing `AttachmentUid`. The stage captures mailbox state, downloads twice, compares both returned metadata and saved bytes to the expected hash/length, and checks UIDVALIDITY/membership/persistent flags again. It retains both files for operator cleanup and never prepares/changes/cleans up messages. Ordinary live `reads` remains metadata-only. Gate/fixture validation is tested synthetically; this stage has not been run against Bridge.
