# Test strategy

Status: Phase 1 includes configuration unit tests, a category-discovery check, and stdio host process tests. Adapter protocol, tool-contract, and live suites below remain planned. Acceptance criteria are tracked in [PLAN.md](../PLAN.md). Safety requirements are in [SECURITY.md](../SECURITY.md).

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

Phase 4 will document the exact opt-in setting and live command when implemented. Do not publish a runnable real-mail command with personal configuration or credentials embedded.

For dependency changes, update the lock file intentionally before validating locked restore. For MailKit changes rerun protocol tests; for Bridge compatibility rerun live acceptance. Record actual results and any skipped tests in [PLAN.md](../PLAN.md).

## Phase 1 coverage and limitations

Synthetic fixtures generate temporary public PEM/DER certificates and a fake password in a private directory. No checked-in credentials or certificates are needed. Tests cover required fields, strict loopback addresses, required TLS modes, allowlists, bounded files, certificate dates/formats, password modes/private parent directory, final-component symlinks, directories/FIFOs, opened-handle replacement, cancellation, and sanitized diagnostics. Owner UID is checked on the opened file and parent directory; a foreign-owner integration case is not exercised because it would require ownership-changing privileges.

The official SDK client initializes the child process without a mailbox service. A separate process test parses captured stdout as MCP traffic and checks stdin-EOF shutdown, exit codes, and empty stderr on success. Startup failures produce only fixed stderr codes. Category discovery rejects missing, duplicate, and unknown categories. No `Protocol` or `LiveBridge` tests exist yet; Phase 1 process tests do not establish any mailbox read-only or Bridge compatibility claim.

Linux is required for the suite, including `mkfifo` for a synthetic special-file regression. Some restricted runners block VSTest's local control socket and MSBuild/compiler IPC; grant runner IPC permission or use an environment that permits it. This does not authorize Bridge access.
