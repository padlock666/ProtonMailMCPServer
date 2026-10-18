# Security boundaries

This is a security design and implementation checklist. Phases 1–3 implement configuration/secret validation, connection-scoped TLS pin enforcement, and read-only mailbox controls with synthetic protocol and official SDK process evidence. A manual real Bridge folder-listing and date-range search smoke test succeeded on 2026-10-18; live persistent flag/membership preservation and failure-path safety remain unverified. Implementation evidence is in [PLAN.md](PLAN.md). That test used the former internal-date filter; sender-date filtering now replaces it and awaits live verification. See [usage](docs/USAGE.md) for the updated inputs.

## Assets and trust model

Protect Bridge credentials, decrypted message content, mailbox membership, and persistent flags, especially `\Seen` and `\Deleted`.

The trusted computing base includes the local OS/user account, this application and dependencies, Bridge, and the launching MCP client. The application receives decrypted mail; Proton's encrypted storage does not make MCP results end-to-end encrypted to a model provider. A hosted client/model may receive tool results according to its own policies.

Stdio limits exposure to the parent/child process relationship; it is not a sandbox against the same OS user. Bridge's generated credentials are not assumed to restrict writes. Read-only safety is enforced by this application's limited interface, adapter behavior, and tests. A compromised process, another client, or a Bridge defect remains outside that guarantee.

## Controls by boundary

| Boundary / risk | Required control | Verification |
| --- | --- | --- |
| MCP caller accesses extra folders | Exact configured allowlist checked for all calls | Denied direct UID and search tests |
| Read silently sets flags | Read-only folder open plus PEEK retrieval | Protocol and real Bridge tests |
| Cleanup deletes flagged messages | No expunge or expunging folder close | Error/cancellation/disconnect transcripts and live flags |
| Credentials sent to wrong service | Literal loopback restriction, required TLS, exact certificate pin | Endpoint/TLS tests before authentication |
| Credentials/content disclosed or trust settings replaced | External owner-only password, configuration, and pin files with private parents and trusted ancestors; sanitized stderr/errors | Permission/path tests and captured output tests |
| Email contains malicious instructions or HTML | Treat content as data; text conversion only; no network/render/execute | MIME and external-reference fixtures |
| Oversized content exhausts resources | Transfer/body/result caps and 30-second deadline | Oversize and cancellation fixtures |
| UID reused after folder reset | Compare UIDVALIDITY before every message read | Stale-reference tests |

## Read-only enforcement

Only expose application operations that list, search, or read. Keep mutable MailKit types private. Never call flag changes, append/copy/move/delete, expunge, folder creation/rename/deletion, or subscription mutation. No SMTP client/configuration is allowed.

Every selected folder opens using `FolderAccess.ReadOnly`, which MailKit implements with `EXAMINE`; require confirmed read-only access. No `SELECT` fallback. Fetch content only with `BODY.PEEK`. Do not request previews that might fetch unapproved body content. Disconnect/dispose without expunging or issuing `CLOSE`.

Protocol tests must fail on unexpected commands, including `SELECT`, `STORE`/`UID STORE`, `APPEND`, `COPY`/`UID COPY`, `MOVE`/`UID MOVE`, `EXPUNGE`/`UID EXPUNGE`, `CLOSE`, `CREATE`, `DELETE`, `RENAME`, `SUBSCRIBE`, and `UNSUBSCRIBE`. Do not rely only on a blacklist or assume a tool annotation prevents writes: fixture scripts should admit only the read/session commands needed for the tested operation.

MailKit's current implementation uses [EXAMINE for read-only selection](https://raw.githubusercontent.com/jstedfast/MailKit/4.18.1/MailKit/Net/Imap/ImapFolder.cs) and [PEEK for body retrieval](https://raw.githubusercontent.com/jstedfast/MailKit/4.18.1/MailKit/Net/Imap/ImapFolderFetch.cs). Test the actual pinned release, including implicit commands and cleanup paths.

## TLS and endpoint policy

- Default host is `127.0.0.1`. Accept only parsed literal loopback IP addresses; reject DNS names, non-loopback addresses, and caller-supplied endpoint overrides.
- Require an explicit TLS mode matching Bridge: mandatory STARTTLS or TLS-on-connect. Do not use opportunistic STARTTLS or plaintext fallback.
- Trust exactly the exported public Bridge certificate and check its validity period. This dedicated pin is the server identity check for the configured local endpoint; it does not depend on trusting a public CA or installing Bridge into the system trust store.
- Implement exact certificate comparison (or SHA-256 fingerprint equivalence) scoped to this client connection. Do not use broad validation exceptions or an accept-all callback. A different certificate must fail even if normally CA-trusted.
- Verify the peer before authentication. A mismatch, missing certificate, or expired/not-yet-valid certificate fails closed. Never learn or replace the trusted certificate automatically from the network.
- When Bridge rotates its certificate, the operator exports/verifies the new public certificate and explicitly updates local configuration. The app never needs Bridge's private key.

Bridge's certificate is self-signed; its [TLS explanation](https://proton.me/support/bridge-ssl-connection-issue) and [settings/export guide](https://proton.me/support/comprehensive-guide-to-bridge-settings) describe this local connection.

## Configuration, secrets, and data handling

Use Bridge's generated IMAP credentials, not the Proton account password. Read the password from a separate regular file outside the repository. Require the password, JSON configuration, and public certificate to be owned by the current user with private modes (for example `0600`), inside private parent directories. Reject symlinks at every path component and unsafe writable ancestors; validate the open directory/file handles rather than relying on a prior path check. Do not write or alter these files on the user's behalf during normal startup.

Only configuration paths belong in launch settings. Keep passwords out of command arguments, environment examples, tracked configuration, shell history, and logs. Do not inspect Bridge's internal keyring/cache files or the user's private key. Use placeholders in documentation and synthetic mail in fixtures.

Configuration has an explicit nonempty folder allowlist. No wildcards, path-prefix matching, automatic descendants, or fallback to all folders. Validate before accessing a requested folder. IMAP's special case-insensitive `INBOX` name is the only required case-normalization exception.

The application has no persistent mail cache, content telemetry, mailbox exports, or attachment files. Normal .NET process memory still contains credentials and plaintext during operations; do not promise secure memory erasure or protection from same-user debugging, swap, or OS crash dumps.

Production logs may contain operation name, correlation ID, duration, and sanitized outcome only. Never log subjects, addresses, folder names, message identifiers, search strings, bodies, credentials, raw exceptions, or protocol transcripts. SDK/framework logging must follow the same policy. Synthetic fixture transcripts may be used in tests.

## Resource limits and untrusted content

Maximum search results: 50, default 20. Maximum selected text-part transfer: 1 MiB, checked both before fetch and during transfer. Maximum returned body: 32,000 characters. Attachment metadata is capped at 100 entries with an explicit truncation flag; subject/address fields at 4,096 characters, filenames at 1,024, and MIME types at 256. Deadline: 30 seconds including queue time. Stop processing on client cancellation; release the serialization gate and dispose the connection safely.

Do not buffer an entire message or attachment to enforce a body-only limit afterward. Bound MIME/conversion work and return explicit metadata-only content status if the selected body cannot be read safely. Keep failures distinct from successful empty searches.

Message text, HTML, headers, filenames, and metadata can contain prompt injection, control characters, links, and misleading instructions. They are untrusted tool data. Descriptions should state that boundary, but the server cannot enforce the downstream model's behavior. Never render HTML, retrieve remote resources, execute mail content, or use filenames as filesystem paths.

## Live testing and maintenance

Real-mail validation is explicit opt-in using externally supplied configuration and an operator-prepared folder. Standard automated tests must never auto-discover or connect to Bridge. The live suite must not send, seed, alter, or clean up mail.

MailKit upgrades require protocol safety regression tests. Bridge compatibility claims require live regression evidence for the tested version. Record versions and outcomes without sensitive data. If behavior deviates from these requirements, fail closed and investigate rather than relaxing a boundary.

No public vulnerability-reporting address or support channel has been established. Do not invent one; report implementation findings through the project's existing private collaboration channel without attaching credentials or real mail.
