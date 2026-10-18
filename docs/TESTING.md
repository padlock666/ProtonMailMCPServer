# Test strategy

Status: Phases 1–3 include configuration/MIME unit tests, category discovery, actual-adapter TLS/IMAP protocol tests, and official SDK tool-contract/stdio process tests. An operator-performed manual real Bridge smoke test succeeded on 2026-10-18 for initialization, tool discovery, folder listing, and date-range search. This is limited functional evidence; body retrieval, live date-boundary exclusion, state preservation, failure/recovery, and version-specific compatibility remain unverified. The explicit opt-in live suite is implemented but has not been run against Bridge. Acceptance criteria are tracked in [PLAN.md](../PLAN.md). Safety requirements are in [SECURITY.md](../SECURITY.md).

Use one xUnit project with distinct unit, protocol, MCP process, and live Bridge groups. Unit/protocol/process tests use synthetic messages and must run without a Proton account, personal credentials, or a running Bridge instance.

## Unit tests

Exercise observable behavior rather than duplicating method implementations:

- Exact folder allowlist enforcement, including empty configuration, case rules for INBOX, and rejection of implicit descendants.
- Configuration and secret-file validation: missing/invalid fields, non-loopback host, unsafe owner/permissions, symlink password path, invalid certificates, and redacted failures.
- Search AND semantics, recipient To/Cc matching, unread tri-state, received-date boundaries, invalid ranges, default/max limits, descending UID results, and truncation.
- Folder + UIDVALIDITY + UID identity, stale epoch rejection, and missing message behavior.
- Main-body choice for plain, HTML-only, multipart alternative, related inline images, attached text, and nested attached messages.
- HTML text conversion without rendering, remote requests, script execution, or attachment traversal.
- Empty/malformed MIME, invalid encodings, missing optional metadata, oversized bodies, actual-versus-advertised transfer size, and 32,000-character output truncation.
- Sanitized error mapping and cancellation propagation; distinguish a failure from an empty successful result.

Use a fake `IReadOnlyMailbox` for tool mapping tests only. It cannot prove the real adapter is read-only.

## IMAP protocol integration tests

Build a scripted loopback IMAP fixture that runs the actual MailKit adapter against expected requests/responses. Use dedicated ephemeral ports, synthetic messages, and test-only TLS certificates. Do not reuse a user's Bridge listener or modify production TLS policy for tests.

The fixture should admit only expected commands and fail on unexpected commands. Verify:

1. Connection negotiation, certificate pinning, required STARTTLS/TLS-on-connect, and authentication ordering.
2. `EXAMINE` with confirmed read-only access for every selected folder; rejection rather than read/write fallback when the server misbehaves.
3. UID-based search/summary/body retrieval and `BODY.PEEK` for all content reads.
4. Body-structure-first retrieval; only the selected text part is downloaded, with no attachment or full-message fetch.
5. Safe logout/disposal with no mutating command or expunging close, including messages already flagged deleted.
6. No access to denied folders and no retrieval after UIDVALIDITY mismatch.
7. Certificate mismatch, expired/not-yet-valid pins, missing STARTTLS, wrong credentials, malformed responses, vanished UIDs, abrupt disconnect, and mid-transfer cancellation.
8. Serialized concurrent calls, cancellation while queued, deadline expiry, gate release, and a fresh usable connection on the next call.
9. Result and transfer limits, including a response larger than advertised.

Capture synthetic wire traffic for assertions. Do not add production protocol logging as a testing convenience. The forbidden-command examples in [SECURITY.md](../SECURITY.md) are a review aid, not a substitute for expected-command checks.

## MCP process tests

Launch the built executable as a child process through the official MCP SDK client, with a temporary synthetic configuration and IMAP fixture. Keep build output out of the protocol stream by launching the built artifact, not an interactive build command.

- Verify initialization, clean shutdown, and discovery of exactly the three approved tools.
- Assert names, descriptions, input/output schemas, and read-only/non-destructive annotations.
- Call each tool end-to-end; check summaries, message identities, text, attachment metadata, and content/truncation status.
- Exercise invalid inputs, denied folders, stale UIDs, missing messages, outages, authentication/TLS failures, timeouts, and cancellation.
- Confirm execution errors use tool-error semantics and do not leak raw exception text.
- Capture stdout and stderr separately. Stdout contains only valid MCP traffic; diagnostic output excludes synthetic sentinel secrets and sensitive field values.

Test contract behavior rather than snapshotting entire SDK-generated descriptions unless the exact schema is deliberately part of the interface.

## Opt-in real Bridge acceptance

These tests require deliberate operator setup and are excluded from the normal automated run. Use an external configuration file, a narrow dedicated folder allowlist, and operator-provided known fixture identities. Merely detecting Bridge or finding credentials is never an opt-in signal.

The operator prepares messages through a separate mail client before the test: at least one unread message, one read message, a text/HTML pair, a message with an attachment, and a message already carrying a deleted flag if Bridge supports that state. This server and suite never create, send, change, move, or delete fixtures. If a required state cannot be represented, record the limitation rather than manufacture evidence.

Procedure:

1. Stop other clients/background rules from changing the test folder and wait for Bridge synchronization to settle.
2. Using read-only access, capture UIDVALIDITY, message UID membership/count, and persistent flags for the dedicated folder. Do not compare session-only `Recent` state as a persistent flag.
3. Repeat folder listing, filtered/unfiltered searches, and reads of the known unread/read messages. Exercise text/HTML conversion and confirm attachment metadata without payload retrieval.
4. Capture the same state again. Require identical UIDVALIDITY, membership/count, and persistent flags; specifically require unread messages to remain unread and deleted-flagged messages to remain present.
5. Exercise Bridge stop/restart, wrong credentials, and a wrong pin using isolated test settings. Never automatically replace the configured trusted pin. Resume correct settings and verify a new call works.
6. Compare state again after recovery. An external change invalidates the comparison; report/repeat under controlled conditions rather than blaming or hiding it.
7. Record tested Linux, .NET SDK, SDK/package, and Bridge versions, test names, date, and outcome. Keep fixture identities, addresses, subjects, credentials, and real transcripts out of the evidence ledger and chat.

No automatic fixture cleanup is permitted. The operator manages the test mailbox separately after the test session. If live configuration is unavailable, mark live acceptance not run and leave Phase 4 incomplete.

## Commands and categorization

The xUnit project uses a `Category` trait: `Unit`, `Protocol`, `Mcp`, and `LiveBridge`. Every test must have one category, with a discovery check to prevent unclassified tests bypassing the live exclusion. Live tests also require an explicit opt-in flag and external configuration; selecting the category alone must not discover credentials or contact a mailbox.

Run from the repository root:

```bash
dotnet restore ProtonMailMcp.slnx --locked-mode
dotnet build ProtonMailMcp.slnx --configuration Release --no-restore
dotnet test ProtonMailMcp.slnx --configuration Release --no-build --filter 'Category!=LiveBridge'
```

The live test is `LiveAcceptance.OperatorPreparedFolderPreservesState`. Category selection without authorization skips it before settings are loaded. Every invocation also checks authorization at runtime. No endpoint/credential discovery occurs. Do not run it until the operator supplies configuration and explicitly authorizes access.

Prepare these **external** files in a private directory outside the checkout (symlink paths resolving into the checkout are rejected):

- A normal application JSON configuration, with exactly one allowed dedicated folder, excluding INBOX. Its password and public-certificate paths must also be external. Existing production secret/TLS validation applies unchanged.
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

`reads` compares full UID membership/count, UIDVALIDITY, persistent flags and keywords; repeats discovery, unfiltered/unread/read searches and all known fixture reads twice; verifies text hashes and attachment metadata; then tests a random incorrect password and a synthetic wrong pin in isolated in-memory settings. It requires the expected sanitized failures and a fresh successful call with correct settings, followed by another state comparison. These negative tests cause one failed authentication attempt; include that in operator authorization. No settings files are changed. The observer uses only read-only selection and UID/FLAGS metadata, with a 30-second per-session deadline; the whole test has a five-minute deadline. Errors intentionally omit live values and inner exceptions.

For a controlled stop/restart, keep the same manifest, stop Bridge yourself, and invoke the same command with `PROTONMCP_LIVE_STAGE=unavailable`. It requires `bridge_unavailable` without retry. Restart Bridge yourself, wait for sync, and invoke with `PROTONMCP_LIVE_STAGE=recovered`; this repeats state/read checks against the original manifest. A stopped-service check is not evidence of an interrupted active transfer.

Separately, using a trusted MCP client and the same dedicated configuration, deliberately stop Bridge during an active read. Require a sanitized `bridge_unavailable` or `timeout`, no retry or fallback, then restart Bridge and require a fresh successful call and a `recovered` stage against the unchanged manifest. Record this manual connection-loss result separately; the suite never controls Bridge or pauses to solicit operator actions. Do not capture live protocol traces. If the interruption missed the active operation, record it as unverified and repeat only with authorization.

A wrong-pin test proves mismatch rejection; actual certificate rotation remains an operator workflow. If rotating Bridge's certificate deliberately, retain the old public pin, require `tls_failed`, independently export/verify the replacement, restart the MCP host with the new external pin, and repeat `recovered`. Never rotate credentials/certificates or repair/reset Bridge automatically. If UIDVALIDITY changes, the old baseline is stale: stop, investigate, and explicitly re-prepare a baseline; do not silently accept new identities.

Keep opt-in variables scoped to a single command. Do not export them globally or place them in test/build tasks. Ordinary tests explicitly exclude `LiveBridge` even when opt-in exists. Record a skipped live test as **not run**, never as passing live acceptance.

For dependency changes, update the lock file intentionally before validating locked restore. For MailKit changes rerun protocol tests; for Bridge compatibility rerun live acceptance. Record actual results and any skipped tests in [PLAN.md](../PLAN.md).

## Phase 1 coverage and limitations

Synthetic fixtures generate temporary public PEM/DER certificates and a fake password in a private directory. No checked-in credentials or certificates are needed. Tests cover required fields, strict loopback addresses, required TLS modes, allowlists, bounded files, certificate dates/formats, password modes/private parent directory, final-component symlinks, directories/FIFOs, opened-handle replacement, cancellation, and sanitized diagnostics. Owner UID is checked on the opened file and parent directory; a foreign-owner integration case is not exercised because it would require ownership-changing privileges.

The official SDK client initializes the child process without a mailbox service. A separate process test parses captured stdout as MCP traffic and checks stdin-EOF shutdown, exit codes, and empty stderr on success. Startup failures produce only fixed stderr codes. Category discovery rejects missing, duplicate, and unknown categories. Phase 2 adds `Protocol` tests; Phase 4 adds the separately gated `LiveBridge` test. The initialization-only process tests establish neither tool contracts nor real Bridge compatibility.

Linux is required for the suite, including `mkfifo` for a synthetic special-file regression. Some restricted runners block VSTest's local control socket and MSBuild/compiler IPC; grant runner IPC permission or use an environment that permits it. This does not authorize Bridge access.

## Phase 2 coverage and limitations

`Fixtures/ScriptedImapServer.cs` listens only on an ephemeral loopback port and generates an isolated synthetic TLS certificate. Ordered session scripts admit exact expected commands, then require socket closure; extra commands or connections fail. Tests execute the actual `MailKitMailbox`, not a mock of `IReadOnlyMailbox`. MailKit's initial namespace/INBOX metadata discovery is included in each authenticated script; only allowlisted selectable folders are returned and only approved folders may be opened.

Protocol evidence covers both required TLS modes, peer pin mismatch/expiry/future validity, absent STARTTLS, authentication rejection, EXAMINE with read-only confirmation, rejection of READ-WRITE or failed EXAMINE, UID-only retrieval, body-structure-first selection, top-level `BODY.PEEK[TEXT]`, nested PEEK sections, attachment-only/unsupported/missing bodies, attachment metadata caps, exact 1 MiB transfers, oversized advertised and actual literals, and output truncation. Search fixtures verify default 20/maximum 50 summaries, descending UIDs, AND filters, To/Cc OR semantics, delivery-date boundaries, unread tri-state, absent metadata, and empty results.

Failure scripts require disposal without CLOSE or any unrequested command, then verify a fresh successful connection. They cover stale/missing/vanished UIDs, malformed protocol replies, rejected fetches, broken sockets, cancellation during a partial literal, cancellation while queued, search timeout, and stalled logout. A controlled deadline clock verifies the production 30-second timer is armed before gate acquisition and that a queued timeout does not release another operation's gate. Pending-connection checks verify queued callers cannot start a concurrent session.

MIME unit tests cover alternative/related primary-body selection, attached text and nested-message exclusion, plain/HTML conversion, empty content, strict UTF-8 and legacy charset handling, malformed transfer encodings, output/control/surrogate bounds, and bounded stream writes. HTML links/images remain data; the conversion contains no renderer or network client. Protocol responses and all credential strings are synthetic and are never produced by application logging.

No Proton account, personal data, Bridge endpoint, or operator credentials are used. These tests establish pinned MailKit behavior against the scripts, not real Bridge compatibility or persistent flag preservation on Bridge. Phase 4 remains incomplete. Search result UID sets and MailKit metadata parsing are not promised constant-memory for arbitrarily large mailboxes; transfer and returned-result caps still apply.

## Phase 3 coverage and limitations

`Mcp/ToolTests.cs` launches the built application through the official 2.2.0 SDK client with temporary synthetic settings pinned to `ScriptedImapServer`. Discovery checks exactly the approved names, descriptions, read-only/non-destructive annotations, strict input schemas, required/optional arguments, output properties and content-status spellings. Calls exercise the actual adapter, structured/text result agreement, absent summary fields, complete message identity, filters, unread tri-state, successful empty searches, attachment metadata and truncation, content statuses, and body truncation.

Input failures are tested before any fixture connection. Runtime fixtures cover denied folders, stale epochs, missing messages/folders, rejected authentication, certificate mismatch, protocol rejection, broken connections, an unavailable ephemeral endpoint, and the real production 30-second deadline. Recovery requires a fresh successful connection. The fixture lifetime is 60 seconds to permit the real deadline test; verification still requires ordered commands and socket closure. No production deadline override or test-only server startup flag is exposed.

The SDK cancellation regression sends an explicit `notifications/cancelled` for a known request ID through `McpClient.SendMessageAsync`, asserts the pending SDK request is cancelled, and requires a successful subsequent call within five seconds. Merely cancelling the high-level SDK call's local token did not reliably deliver server cancellation in this environment; do not infer prompt remote cleanup from a cancelled local await. Received cancellation notifications propagate to the adapter. Captured raw stdio tests cover complete tool results and stdin EOF during a stalled LOGOUT: EOF drains the active operation under its existing 30-second deadline, then exits cleanly. It does not promise immediate cancellation on EOF. Child-process termination also closes its sockets; no CLOSE/expunge command is admitted.

SDK client process tests use the initialize-handshake revision `2025-11-25`; the existing default-options client initialization/discovery test is retained as well. Every captured tool diagnostic must match a fixed grammar of operation/correlation/duration/outcome, excluding sensitive fixture values. Separate stdout capture parses every line as MCP JSON and checks the result. Package versions/locks and the adapter implementation are unchanged. These tests establish the MCP contracts and pinned adapter behavior against synthetic scripts, never Bridge compatibility or live flag preservation.
