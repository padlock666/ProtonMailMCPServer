# Attachment downloads — design and implementation plan

Status: **implementation authorized on 2026-10-21 and implemented; synthetic verification recorded in PLAN**. The owner requested downloading an attachment into a configured private directory and returning its path and metadata, so that the local AI client can use its own document tools. This document defines that scope change. Live Bridge downloads and intended-client PDF interpretation remain separate acceptance work; [PLAN.md](../PLAN.md) tracks their status.

## Product behavior and scope

The user asks the AI to read an attached PDF. The AI searches for the message, calls `get_message` for attachment metadata, calls `download_attachment` with one attachment reference, and passes the returned absolute path to its PDF tools. The server saves the original attachment bytes after MIME transfer decoding. It does not extract PDF text, render pages, perform OCR, unlock encrypted PDFs, unpack archives, or launch files. Scanned and password-protected PDFs can be downloaded just like other files; interpretation belongs to the client.

This is a general attachment download; PDF interpretation belongs to the client. Support individual leaf MIME parts explicitly classified as attachments, including PDF, text, image, Office, archive, and arbitrary binary payloads. Exclude multipart attachment containers, attached messages (`message/rfc822` and other `BodyPartMessage` nodes), descendants of those containers/messages, and inline parts that are not explicitly attachments. These exclusions prevent a download from becoming full-message export or another route to the main body. No bulk download, attachment search, resource endpoint, HTTP listener, remote URL, caller-selected save path, or deletion tool is introduced.

The client must have permission to read the same filesystem location. A path on this Linux host does not deliver a PDF to a separate cloud sandbox. Client compatibility requires a synthetic end-to-end test before claiming the AI can read downloaded PDFs.

## Configuration and enablement

One optional configuration object is read at startup alongside the existing settings:

```json
{
  "AttachmentDownloads": {
    "Directory": "/absolute/private/directory/attachments"
  }
}
```

This is an optional fragment to add to a complete external configuration. Omit `AttachmentDownloads` to disable downloads. When omitted, register exactly the existing three tools and do not inspect, create, or lock a download directory. When present, require a non-null object with exactly one nonblank `Directory`; reject null, unknown/duplicate fields, and invalid paths at startup. No implicit default directory, environment override, or separate `Enabled` switch. When enabled, valid configuration registers exactly four tools; restart is required to enable, disable, or change the directory.

The operator creates a dedicated directory outside the checkout and outside the settings/credential directory. Require a local filesystem supporting the Linux operations below. The directory must already exist, be owned by the effective user, and have mode `0700` without special bits. Require an absolute, control-free path of at most 3,000 UTF-8 bytes, without `.`/`..` components, symlinks, or trailing slash; `/` is invalid. Ancestors follow the existing trusted-owner/non-writable rules, including the root-owned sticky-directory exception. Do not create or chmod the configured directory. Reject a download directory that is the same as, or an ancestor of, the loaded configuration, password, or certificate path. Repository placement remains an operator rule, as with existing settings; do not search for repository roots.

Open and validate directories by handle using the existing Linux protected-path approach. Hold a close-on-exec directory handle and a nonblocking exclusive advisory lock for the server lifetime. A second server using that directory fails startup. Validate path-to-handle device/inode identity and permissions before every download and before publication; fail if the directory was replaced, moved, or made unsafe. The lock coordinates participating server processes; it is not protection against another program running as the same user. Network filesystems and same-user hostile filesystem changes are outside the supported storage model.

Startup codes specific to the feature: `configuration_attachment_downloads_invalid`, `configuration_download_directory_unsafe`, and `configuration_download_directory_in_use`. Malformed JSON/unknown fields use the existing generic configuration code. Emit fixed codes only, without paths or native error text.

## Attachment discovery and identity

Extend each existing `get_message.attachments[]` entry with nullable `attachmentId`. Preserve `filename`, `mimeType`, `size`, ordering, the 100-entry cap, and `attachmentsTruncated`. Set the ID only when downloads are enabled and the entry satisfies the eligibility and identifier bounds below; otherwise return null. A non-null ID indicates an eligible part, not that the file will fit size/storage limits. `get_message` continues to fetch only its selected text body and attachment metadata.

An attachment reference is **folder + UIDVALIDITY + UID + attachmentId**. The ID is a canonical numeric MIME part identifier, for example `2` or `2.1`, with 1–16 positive decimal components, no leading zeroes, each at most `uint.MaxValue`, and at most 128 ASCII characters total. For a top-level single-part attachment whose MailKit part specifier is empty, expose `1`; resolve it internally to `TEXT`. For multipart messages, use the actual numeric part specifier. Never accept `TEXT`, `HEADER`, section suffixes, offsets, whitespace, or raw IMAP syntax from the caller.

Construct IDs from the fresh BODYSTRUCTURE, and map input back to an eligible node before retrieving bytes. Do not pass the caller's string straight to MailKit. Walk without descending into attached messages or attachment containers; check attachment disposition and the path to the candidate, rather than trusting a part number alone. Bound traversal at 1,024 nodes and depth 16; a download exceeding these bounds fails with `attachment_unsupported`. If building the ID map for `get_message` exceeds these bounds, return null IDs without changing its existing body/metadata behavior. Existing MailKit metadata parsing precedes these bounds and is not claimed constant-memory.

IDs have no process-lifetime key and survive restart while the message identity remains valid. They are not capabilities and do not grant folder access. Listing first is the normal workflow, but no issued-ID registry is maintained: direct calls can retrieve any eligible part proven by fresh structure, including an eligible part beyond the metadata list's first 100 entries. There is no new discovery pagination in this scope. Duplicate or absent filenames do not affect identity. Reject ambiguous duplicate structure identifiers as `protocol_failure`.

## Tool contract

Tool name: `download_attachment`. Required arguments only:

```json
{
  "folder": "INBOX",
  "uidValidity": 7,
  "uid": 42,
  "attachmentId": "2"
}
```

Use existing folder and positive unsigned 32-bit validation. Reject unknown fields, including `savePath`, `directory`, `filename`, `overwrite`, and encoding/size overrides, before any I/O. Validate the folder policy before filesystem work or connection, and again against the resolved folder name before opening it. Disabling downloads must also be enforced at the application boundary, even though the tool is absent from discovery.

A successful structured result has exactly these fields (with equivalent JSON text content, as for existing tools):

```json
{
  "reference": {
    "folder": "INBOX",
    "uidValidity": 7,
    "uid": 42
  },
  "attachmentId": "2",
  "path": "/absolute/private/directory/attachments/attachment-0123456789abcdef0123456789abcdef.pdf",
  "originalFilename": "example.pdf",
  "mimeType": "application/pdf",
  "reportedSize": 1400,
  "sizeBytes": 1000,
  "sha256": "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"
}
```

All example values, including the hash, are synthetic placeholders. `sizeBytes` is the exact decoded file length; `reportedSize` is the BODYSTRUCTURE octet count and can differ. `sha256` is 64 lowercase hex characters computed over the saved decoded bytes, for integrity checking rather than authenticity. `originalFilename` is nullable, sanitized and capped at 1,024 characters; `mimeType` is sanitized and capped at 256. MIME type and filename are sender/server claims, not content validation. The file is never silently truncated: return a complete file or an error, with no partial-success result.

Generate a cryptographically random 128-bit filename component unrelated to mailbox identifiers or original names. Use `attachment-<32 lowercase hex>.pdf` for declared MIME type `application/pdf` (case-insensitive) and `.bin` for every other type. This preserves convenient PDF recognition without using an attacker-supplied filename as a path. An extension is not a safety or validity guarantee; the client must treat the bytes as untrusted. Files have mode `0600`, never executable bits. The generated absolute path is returned only after publication; it is not a URI or remotely accessible link.

The tool description must state that it writes a local file, retains it until operator cleanup, requires a client with access to that path, and never executes attachments. Set `ReadOnlyHint=false`, `DestructiveHint=false`, `IdempotentHint=false`, and retain the existing closed-system `OpenWorldHint=false`. Mailbox access remains read-only; creating a new local file is a side effect. Existing tools retain their annotations.

## Limits and retention

Limits are fixed constants, not new configuration knobs:

| Limit | Value and behavior |
| --- | --- |
| Encoded selected-part transfer | 36 MiB (37,748,736 bytes); check advertised octets and actual literals/writes |
| Decoded file | 25 MiB (26,214,400 bytes); enforce while decoding/writing |
| Directory contents | 250 MiB (262,144,000 bytes) of logical file lengths and 1,000 entries, including incomplete files |
| Whole operation | Existing 30 seconds including queue, storage checks, network, decoding, logout, and publication |
| Existing message-body transfer | Still 1 MiB; the download cap applies only to the selected attachment operation |

The encoded and decoded limits are independent. Highly expanded quoted-printable content can hit the wire limit while its decoded file would be under 25 MiB. Never switch encoding, fetch an alternate part, or retry with a larger limit to bypass a rejection.

Before connecting, enumerate at most 1,001 directory entries through the validated directory handle, checking metadata without following links or reading file contents. Count all regular files, including operator-added and abandoned `.part` files. Reject unexpected directories, symlinks, special files, files with multiple hard links, wrong ownership, or non-private modes as unsafe storage; never delete or traverse them. Reserve capacity conservatively for one 25 MiB output and one additional entry. Refuse the operation if this would exceed either cap, even if the requested file might be smaller. Before publication recheck actual totals including the current temporary file, without reserving a second file; rename adds no entry. This quota bounds this server's writes under the dedicated-directory/lock assumptions; it is not an OS quota or a free-disk-space guarantee. Handle disk-full and permission failures safely.

Completed files persist across process exit and restart until the operator removes them. There is no TTL, cache reuse, index, background sweeper, or automatic deletion of completed files. Each successful repeat creates a distinct file and consumes quota; hashes can identify duplicates. Retention and quota recovery belong in operator documentation. Keep downloaded plaintext outside source control, backup/sync folders unless intentionally chosen, and configuration/secret storage. The server does not promise secure erasure or protection against same-user access, swap, backups, or crash dumps.

## Retrieval, publication, and failure handling

1. Validate arguments, enabled configuration, and requested folder policy. Enter the same mailbox serialization gate under the existing deadline. Validate directory identity, private metadata, and available quota before making a connection.
2. Create one connection; enforce encrypted loopback and the configured certificate pin before authentication. Resolve/check folder policy and identity, use `EXAMINE`, and verify read-only access. Compare UIDVALIDITY before fetching the requested UID's BODYSTRUCTURE. Distinguish a missing message from a missing attachment.
3. Resolve the eligible part from that structure. Reject excessive reported size or unsupported transfer encoding before content fetch. Fetch only the selected section using the typed UID-based MailKit API and `BODY.PEEK`; never fetch the complete message, another attachment, or use sequence-number addressing.
4. Use an operation-specific bounded stream factory: the existing body path stays at 1 MiB; the attachment path admits at most 36 MiB total encoded bytes for the selected fetch. Enforce actual literal size before allocation and aggregate writes/stream allocations, so extra literals cannot multiply the allowance. Reject unexpected sections, offsets, duplicate payloads, or UIDs when known. MailKit may initially call the factory without a UID; validate the committed stream's UID/section before using it. Require complete receipt of the literal and agreement with the reported BODYSTRUCTURE octet count; an under-cap mismatch is `attachment_malformed`, and exceeding a cap is `attachment_too_large`. Use one encoded buffer whose capacity cannot grow beyond the vetted allowance, then decode to disk in chunks; never allocate a second full decoded buffer. This is bounded buffering, not constant-memory streaming.
5. Create a random hidden `.part` file in the held directory with exclusive, no-follow creation and mode `0600`. Validate the opened regular-file handle and link count. Transfer-decode `base64`, `quoted-printable`, `7bit`, `8bit`, or `binary` only (absent encoding means `7bit`). Preserve decoded bytes exactly: no charset conversion, newline normalization, PDF processing, or decompression. Invalid encoding must fail, not produce repaired/truncated bytes. A narrow streaming transfer decoder enforces complete/canonical base64 padding, complete quoted-printable escapes and 7bit byte bounds; it does not use a document parser or MimeKit’s permissive transfer decoding. Enforce decoded size and cancellation per chunk; compute SHA-256 as bytes are written. Valid zero-byte attachments are permitted.
6. Flush and close the temporary file, finish safe IMAP logout/disposal, recheck cancellation/deadline and storage, and prepare the result before publication. Hold the gate throughout. Publish using a same-directory atomic rename that refuses an existing destination (`renameat2` with `RENAME_NOREPLACE`). Generate a new name on collision, with at most three attempts. Never fall back to overwrite or copy-and-delete publication; unsupported filesystem operations fail closed.
7. Successful publication is the commit point. After it, retain the completed file and return its result without another fallible mailbox operation or cancellation check that would deliberately turn success into failure. Release the gate/handles safely. A cancellation, transport failure, or process death around this point can still prevent the client receiving the response; the completed file may remain. Exactly-once delivery and retry deduplication are not promised. No power-loss durability guarantee is made beyond normal OS filesystem behavior.

If an otherwise healthy session returns no selected payload after successful structure lookup, perform one UID-only metadata existence check under the same deadline: absent UID means `message_not_found`, present UID means `attachment_not_found`. Do not retry the payload or open another connection. Broken/protocol-rejected sessions retain their existing error category and dispose immediately.

Before publication, every failure/cancellation attempts to unlink only the temporary file created by this operation, using the held directory and known identity; do not use a cancelled token to skip this cleanup. No unbounded retries. If cleanup fails, retain the private `.part` file, count it against quota, and emit fixed outcome `download_cleanup_failed` without its name/path. For client cancellation preserve cancellation semantics while recording this cleanup outcome. Sudden process termination can leave `.part` files; operators remove these while the server is stopped. Do not scan/delete them automatically at restart.

The deadline and cancellation checks cover each asynchronous operation and each decode/enumeration chunk. Native filesystem calls cannot guarantee interruption of a blocked kernel operation; the 30-second deadline is not a hard real-time guarantee. This is another reason to require a supported local filesystem. Cleanup consists of bounded local attempts rather than a new network operation or a fresh long-running deadline.

No logs or errors contain attachment IDs, paths, filenames, hashes, MIME metadata, file contents, native exceptions, or production wire traffic. Successful tool results intentionally disclose the path and requested metadata to the trusted client. Folder exclusions continue to restrict access paths rather than copies reachable through permitted labels.

## Errors

Keep MCP tool-error semantics with fixed snake-case codes and no path/content details. Existing `invalid_input`, `denied_folder`, `folder_unavailable`, `stale_reference`, `message_not_found`, TLS/authentication/Bridge/protocol failures, and `timeout` retain their meaning. Local storage exceptions must not be caught as `bridge_unavailable` by the adapter's current broad `IOException` mapping.

| New code | Meaning |
| --- | --- |
| `attachment_downloads_disabled` | Application-boundary call while the feature is disabled; MCP discovery omits the tool |
| `attachment_not_found` | Valid part ID does not resolve to an attachment, including a primary-body/inline-only target |
| `attachment_unsupported` | Attachment is a container/message, structure exceeds bounds, or encoding is unsupported |
| `attachment_too_large` | Advertised/actual encoded or decoded size exceeds a cap |
| `attachment_malformed` | Invalid transfer encoding data or incomplete payload; no file published |
| `download_directory_unsafe` | Directory identity/permissions or its entry metadata no longer meets policy |
| `download_limit_exceeded` | Directory byte/entry capacity cannot reserve another download |
| `download_storage_failed` | File creation/write/flush/publication fails, directory is unavailable, or collision retries are exhausted |
| `download_cleanup_failed` | Pre-publication failure leaves an incomplete file that could not be removed |

If MailKit discards an unexpected-UID literal without committing it, a missing-stream guard rejects the response as `protocol_failure` instead of treating it as absence. A malformed IMAP response/disconnection still follows the existing Bridge/protocol error categories rather than inventing a precise MIME failure. A supplied ineligible container identifier can produce `attachment_unsupported` even though discovery returns null for that entry. Missing/unsafe storage and cleanup failures must be tested independently from network failures.

## Implementation shape and sequence

Keep one application project and one xUnit project. Add immutable request/result DTOs and `DownloadAttachmentAsync` to the small `IReadOnlyMailbox` interface. Inject one narrowly scoped private-download store into the adapter for directory validation, quotas, temporary-file ownership and publication; do not introduce a repository, database, or general storage framework. MailKit objects and MIME transfer decoding stay inside `Mail/`; only DTOs cross to `Tools/`. The native directory-entry reader requires a 64-bit Linux process. Reuse protected-handle primitives without weakening validation of configuration, credentials, or certificate files.

1. **Configuration and storage.** Implement optional settings and lifetime directory ownership/lock, protected creation, quota reservation, exclusive publication, and cleanup. Add focused filesystem tests using synthetic private temporary directories. Keep the tool disabled until integration is complete.
2. **Identity and bounded retrieval.** Implement attachment eligibility/IDs, DTOs, scoped stream budgets, strict byte decoding, and adapter download flow. Preserve the body path and its cap. Refactor session completion only enough to allow publication after logout and before gate release; do not publish inside the current callback and then run the existing post-logout cancellation check.
3. **Protocol safety.** Add real-adapter scripted TLS/IMAP tests for eligible parts, top-level/nested section addressing, fresh identity/policy checks, byte integrity and all failure/cancellation paths. Verify actual commands and bounded allocation behavior using the pinned libraries.
4. **MCP integration.** Register the fourth tool only when configured, add nullable metadata IDs, strict input/output schemas, fixed error codes and accurate annotations. Add SDK process tests for discovery, results, disk effects, sanitization, EOF/cancellation and recovery.
5. **Documentation and synthetic verification.** Update DESIGN, SECURITY, AGENTS, CONFIGURATION, USAGE, TESTING, README, BACKLOG, and PLAN to describe implemented behavior only when it exists. Add optional settings to example guidance without enabling downloads by default. Run the locked restore, Release build, full non-live suite, and documentation checks from [DEVELOPMENT.md](DEVELOPMENT.md). Keep SDK/package pins unless a separately reviewed dependency change is required.
6. **Client and Bridge acceptance.** First use a synthetic PDF and the intended local client to verify that the returned path can actually be read by its document tools. Separately, with explicit operator authorization and external fixtures, extend/run live acceptance to verify attachment hashes and unchanged message membership/flags. Record exact versions and limitations; leave unrun acceptance unchecked.

## Acceptance matrix

| Area | Required evidence before completion |
| --- | --- |
| Disabled compatibility | Existing configuration works; exactly three tools; no download directory access; old request shapes/results preserved apart from additive nullable metadata field |
| Enabled configuration | Exactly four tools; bad/null/unknown settings and unsafe paths fail at startup without mailbox access or sensitive diagnostics; directory lock conflicts fail closed |
| Identity/eligibility | Duplicate/null filenames, top-level and nested PDFs, restart, stale epoch, vanished UID/part, direct calls, primary-body/inline/container/message exclusion, traversal bounds and ambiguous IDs |
| Bytes | Synthetic PDFs compare byte-for-byte and SHA-256 after base64/quoted-printable decoding; binary/8bit/7bit and empty files; malformed encodings rejected; PDF bytes never parsed or executed |
| Limits | Exact and one-over encoded/decoded caps, dishonest advertised lengths, multiple/unexpected literals, body cap unchanged, directory quota at boundaries, bounded entry scans and no partial success |
| Filesystem | `0700`/`0600`, hostile filenames, symlinked ancestors/leaves, hard links, pre-existing targets, collisions, concurrent processes, restrictive umask, root replacement/mode changes, disk-full/flush/rename failures |
| Protocol | Only expected read/session commands; UID-based `EXAMINE`/PEEK; pin before authentication; no full-message fetch, mutation or expunge across success/errors/cancellation/disconnect |
| Lifecycle | Queue/in-transfer/decode/pre-commit cancellation, stalled logout, cleanup success/failure, abrupt termination and quota counting of leftovers, commit/response-loss semantics, serialized recovery |
| MCP/privacy | Accurate side-effect annotations; equivalent structured/text results; bounded metadata; errors and stderr omit sentinel secrets, paths, hashes and filenames; stdout remains protocol-only |
| Client handoff | Intended client reads a synthetic downloaded PDF using the returned path; record filesystem/sandbox limits and client version; no universal client-compatibility claim |
| Live Bridge | Explicit opt-in download stage, known attachment hash/length and before/after persistent flags/membership; unread and deleted-flagged fixtures where supported; no mailbox preparation/cleanup by server |

The live download stage has its own explicit opt-in in addition to the existing read-only Bridge gate because it retains plaintext on disk. Require an operator-provided dedicated directory and externally known attachment identity/hash/length; ordinary live read tests remain metadata-only. The operator manages completed live files and mailbox fixtures. Synthetic test teardown may remove only its own synthetic files. The stage is implemented but has not been run against Bridge. Existing reads do not construct a download store or fetch attachment payloads.

## Evidence and references

Design review used the current `MailKitMailbox`, `BoundedImapClient`, `LinuxProtectedFile`, configuration loader, tool registration and DTOs. The restored MailKit/MimeKit 4.18.1 XML documents expose `CreateStream`, UID/section `GetStreamAsync`, attachment metadata and `DecodeToAsync`; these extension points are exercised by the real-adapter synthetic regressions. Transfer decoding uses the narrow strict decoder in `AttachmentParts`, preserving binary bytes without charset/document parsing. See PLAN for actual commands/results.

Linux primitives: handle-relative exclusive creation and no-follow flags are described in [open(2)](https://man7.org/linux/man-pages/man2/open.2.html); atomic no-replace publication in [rename(2)](https://man7.org/linux/man-pages/man2/rename.2.html); process coordination in [flock(2)](https://man7.org/linux/man-pages/man2/flock.2.html). Synthetic filesystem tests exercise these operations on the verification host; unavailable operations fail closed rather than substitute weaker ones.

Existing projects informed the functional choice: [gmail-attachments-mcp's download implementation](https://github.com/zayansalman/gmail-attachments-mcp/blob/main/src/gmail_attachments_mcp/client.py) saves decoded bytes and returns paths; [outlook-mcp](https://github.com/mpalermiti/outlook-mcp#attachments) documents a configured attachment directory. These are precedents, not adopted code or evidence that they meet this project's safety requirements.
