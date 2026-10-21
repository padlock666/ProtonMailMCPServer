# Message search pagination

Status: implementation requested on 2026-10-21. The adapter and MCP contracts implement this design; synthetic verification evidence is recorded in PLAN.md. Live Bridge pagination acceptance remains pending. This document extends the [approved design](../DESIGN.md); [PLAN.md](../PLAN.md#message-search-pagination--design-requested-2026-10-21) tracks the implementation checklist. Existing mailbox [security boundaries](../SECURITY.md) remain mandatory.

## Outcome and scope

Let a caller traverse matching messages in one permitted folder through successive `search_messages` calls. Preserve descending UID order, existing filters, default page size 20, maximum page size 50, and the three existing MCP tools. A caller that omits pagination inputs continues to receive the first page.

Use a forward-only cursor based on an exclusive UID boundary. Each page is a new serialized, read-only mailbox operation with its own connection and existing 30-second deadline. There are no page numbers, offsets, previous-page tokens, total counts, cross-folder results, retained result sets, new configuration, or new package dependencies. A client can retain earlier pages itself.

This is traversal of a changing mailbox, not a transactional snapshot. With unchanged membership and filter matches, following cursors visits every matching UID exactly once. Guarantees concern folder + UIDVALIDITY + UID, not deduplication of the same underlying message across labels.

## MCP contract

Keep required `folder` and existing optional `sender`, `recipient`, `subject`, `sentStart`, `sentEnd`, `unread`, and `limit`. Add optional `cursor`, a string or null. Omission/null starts a new traversal; a non-null cursor continues one. Empty, whitespace, oversized, malformed, or invalidly authenticated strings return `invalid_input`.

Every continuation repeats the same folder and filters. Omitted and null filters are equivalent. Normalize only the special case-insensitive `INBOX` name; other folder names and substring values must match exactly, without trimming, case folding, or Unicode normalization. Date binding uses calendar dates, and unread retains its null/true/false distinction. A caller can change `limit` between pages within 1–50; omitting it always uses 20. Changing any search filter requires starting without a cursor.

Extend the result with an always-present nullable `nextCursor`:

```json
{
  "messages": [],
  "truncated": false,
  "nextCursor": null
}
```

`truncated` remains the indication that the page's UID search found more matching candidates than the requested limit. Define the invariant `truncated == (nextCursor != null)`. It is not a promise that another page will still contain messages when requested. A final or initially empty page has `truncated: false` and `nextCursor: null`.

Messages can disappear between search and summary fetch, so a page can contain fewer than `limit` messages, including zero, while still returning a cursor. Clients stop on a null cursor, **not** on a short or empty `messages` array. Do not refill short pages with additional searches/fetches.

Synthetic client flow:

```json
{"folder":"INBOX","subject":"synthetic pagination fixture","limit":2}
```

Copy the returned cursor unchanged into the next call, repeating the filters:

```json
{"folder":"INBOX","subject":"synthetic pagination fixture","limit":2,"cursor":"<opaque nextCursor from previous result>"}
```

The placeholder is illustrative and is not a valid token. Do not decode or construct cursors in clients. This is tool-argument pagination, not MCP transport-level pagination of tools/resources.

Existing valid requests remain valid and keep their ordering and limits. The extra output property is additive, but clients that strictly reject unknown result fields need an update. Refresh tool discovery after upgrading. Earlier releases reject the cursor input; [USAGE.md](USAGE.md) documents the implemented workflow.

## Cursor format, binding, and lifetime

Use a small internal cursor codec with a cryptographically random 32-byte key generated once per mailbox service/process lifetime. Keep only this key in memory; do not retain searches, messages, cursor registrations, or connections. Never derive the key from Bridge credentials or persist it. All calls served by that mailbox instance share the key; tests can supply an isolated codec/key through internal dependency injection.

Version 1 is canonical unpadded base64url of a fixed binary envelope:

| Field | Size | Meaning |
| --- | --- | --- |
| Version | 1 byte | Value 1 |
| UIDVALIDITY | 4 bytes | Positive unsigned integer, big-endian |
| Before UID | 4 bytes | Positive unsigned integer, big-endian; exclusive boundary |
| Query binding | 32 bytes | HMAC-SHA-256 of the canonical folder and filters |
| Authentication tag | 32 bytes | HMAC-SHA-256 over the preceding 41 bytes |

The decoded envelope is 73 bytes and its canonical encoded form is 98 ASCII characters. Reject any other length/alphabet/version, padding, noncanonical encoding, invalid tag, or zero identifier before mailbox I/O. Compare tags and query bindings in constant time. Use separate domain prefixes (`protonmcp-search-binding-v1` and `protonmcp-search-cursor-v1`, each followed by a zero byte) for the two HMAC operations using the same process key.

Canonical query bytes are UTF-8 JSON for a fixed-order array `[folder, sender, recipient, subject, sentStart, sentEnd, unread]`, using the existing strict serializer, explicit nulls, normalized INBOX, and ISO `YYYY-MM-DD` date strings. Serialize values; never concatenate caller strings with delimiters. Exclude `limit` and `cursor`. Binding uses the validated request folder; the adapter also checks that resolution returns the same name under the INBOX exception, failing with `protocol_failure` if not. Both requested and resolved names must pass the folder policy.

The keyed binding avoids exposing raw filters or an unkeyed digest that a token holder could trivially compare with guessed filters. Tokens are authenticated, **not encrypted**; UID fields can be decoded. Treat the entire cursor as sensitive mailbox metadata: return it only in the intended tool result, and exclude it, the key, and all decoded fields from logs, errors, evidence, and real-data fixtures. A token is never authorization to access a folder.

Cursors have no wall-clock expiry in version 1. They remain usable for the current process lifetime while the folder epoch and query match. A restart or another server instance has a different key and returns `invalid_input`; start again without the cursor. Replay is allowed and repeats a range search against current state, so it is neither a snapshot nor an idempotent response guarantee. No cross-process continuation or automatic restart/retry is promised.

## Adapter algorithm

1. Validate the ordinary request and limit, and enforce the requested-folder policy. Decode/authenticate/bind any cursor before opening a connection. Denied folders fail as `denied_folder`; invalid filters/tokens fail as `invalid_input`.
2. Enter the existing serialized operation with cancellation/deadline propagation. Establish a fresh connection with mandatory loopback TLS and certificate pinning before authentication. Resolve and recheck the folder, open with `FolderAccess.ReadOnly`, and verify access.
3. Require a positive current UIDVALIDITY. For a continuation, compare it with the token **before search or summary fetch**. A mismatch returns `stale_reference`; never reinterpret the boundary in a different epoch. A missing/nonselectable folder remains `folder_unavailable`.
4. Build the existing typed AND query. On continuation, add a typed UID restriction for the inclusive range `1` through `beforeUid - 1`. If `beforeUid == 1`, return a terminal empty page after policy/epoch validation without issuing an invalid zero/reversed range. Do not use sequence-number ranges or `*` as an upper bound.
5. Perform UID search. Validate positive returned UIDs and the continuation boundary; an out-of-range response fails with `protocol_failure`. Deduplicate and order matching UIDs descending. Select at most `limit` candidates and determine whether another candidate exists. The initial page has no boundary.
6. Fetch existing summary fields for the selected UIDs only, at most 50. Exclude unsolicited summaries outside that set, avoid duplicate output UIDs, and return surviving summaries in descending UID order. No bodies, previews, or attachment payloads are fetched. Missing summaries represent vanished candidates and do not trigger retries or backfill.
7. If additional candidates existed at search time, sign a cursor using the opened folder's UIDVALIDITY and the **lowest selected candidate UID**, even if that candidate disappeared during fetch. Otherwise return null. Never advance using only the lowest successfully fetched summary; that can repeat work or prevent progress on an empty page. A page's next boundary must strictly decrease on continuation.
8. Finish through existing safe LOGOUT/disposal and post-logout cancellation checks. Do not return a partially successful page or cursor after a failed/cancelled operation. Preserve existing sanitized failure mapping and gate release.

Synthetic example: matching UIDs `90, 70, 50, 30, 10`, limit 2, yield candidate pages `[90,70]`, `[50,30]`, `[10]`. Boundaries are 70 and 30. Arrival of UID 100 after page one cannot enter a later page. If 70 disappears during the first fetch, that page still advances below 70; if both selected candidates disappear, it can return an empty page with that same continuation boundary.

UID ranges are inclusive and can denote the same set in either endpoint order; the explicit lower-bound guard matters. The standard also defines UIDVALIDITY for detecting reused identifiers. See [IMAP UID semantics](https://www.rfc-editor.org/rfc/rfc9051.html#section-2.3.1.1) and [UID commands](https://www.rfc-editor.org/rfc/rfc9051.html#section-6.4.8). MailKit provides a [typed UID search criterion](https://mimekit.net/docs/html/M_MailKit_Search_SearchQuery_Uids.htm). Compact `UniqueIdRange` construction and the typed search criterion are checked against restored **4.18.1** XML documentation and emitted commands in actual-adapter protocol tests; current online documentation alone is not evidence for the pinned release. Do not expand the numeric range into an array of every possible UID.

## Concurrent changes and resource limits

Each page evaluates filters again. Removed messages disappear; UID gaps are normal. New arrivals receive higher UIDs in the same epoch and cannot shift later pages. Flag changes can make older messages enter or leave an unread-filtered traversal: a newly matching UID below the current boundary can appear, while one above it will be missed. Restart the search to see new arrivals or refresh changed matches. Repeated/replayed cursors may overlap earlier responses; uniqueness applies to following each newly returned cursor once.

Pagination bounds returned summaries and summary fetches per call. The ordinary IMAP search may still materialize every matching UID within the remaining range, and the initial search can still cover the whole folder. This implementation does not establish constant-memory searches, bounded server search work, or a total traversal deadline. Keep the existing per-operation deadline and cancellation. Do not introduce ESEARCH/SORT requirements, cached snapshots, automatic multi-page loops in the server, or dependency upgrades to claim a performance improvement.

## Acceptance matrix

| Area | Required evidence before declaring implementation complete |
| --- | --- |
| Traversal | Stable synthetic mailbox with more than 50 matches; sizes 1/20/50; empty, exact-limit and limit-plus-one counts; sparse UIDs; exact descending membership with no duplicate/omitted UID across the full chain; terminal null cursor |
| Binding | Every filter and folder mismatch rejected before I/O; omission/null equivalence; INBOX exception; exact other names/strings; unread tri-state; page-size changes allowed |
| Token safety | Round trip, malformed alphabet/length/padding/version, noncanonical encoding, altered payload/tag, zero IDs, wrong key/restart; fixed safe errors without token leakage; uint maximum and boundary 1 |
| Concurrent changes | Higher arrivals, gaps, boundary removal, selected-candidate disappearance (including all candidates), changed unread matches, replay, and an empty continuation after remaining messages disappear |
| Policy and identity | Allowlist and AllExcept checks on every page, denied request before connection, rejected resolved-name mismatch, stale UIDVALIDITY before UID search/fetch, missing folder; no cursor bypass of policy |
| Wire safety | Actual pinned adapter uses EXAMINE and UID search with the finite range, fetches only selected UID summaries, and admits no mutation/body-fetch/CLOSE commands; retain existing PEEK regressions for get_message |
| Failure and recovery | Cancel while queued/searching/fetching/logging out, timeout, rejected search/fetch, malformed reply, broken socket, and fresh successful call afterward; no success cursor on failure |
| MCP contract | Exactly three tools, strict optional cursor schema and 98-character bound, nullable nextCursor, invariant with truncated, equivalent structured/text JSON, legacy first-page calls, multi-page SDK calls, sanitized stderr and protocol-only stdout |
| Live compatibility | Separately authorized read-only traversal of an operator-prepared dedicated folder at a small page size; compare full expected UID membership and persistent flags before/after; record tested versions without mailbox values |

The live fixture already allows 2–50 messages, so using `limit: 1` exercises continuation without broadening its manifest limits or policy. Its read stage now traverses each unfiltered/unread/read result at `limit: 1`, retaining the existing opt-in gate and checking expected identities. Do not mutate live fixtures to test races; those cases belong in synthetic scripts. Synthetic passing results do not establish Bridge compatibility, and unrun live acceptance leaves the MVP incomplete.

## Implementation touchpoints

The implementation adds optional cursor to `SearchRequest` and nullable next cursor to `SearchResult` in `Contracts/MailContracts.cs`; retains the three methods in `Mail/IReadOnlyMailbox.cs`. A small internal codec beside the mail adapter retains only the process key. `Mail/MailKitMailbox.cs` validates cursors, restricts UID ranges, and advances from selected candidates. The codec uses the existing singleton dependency-injection lifetime.

`Tools/MailboxTool.cs` exposes cursor input mapping, schema bounds, result schema, and pagination guidance. Unit, Protocol, Mcp, and gated LiveBridge suites cover the extension. Package versions/locks, configuration, and launch behavior are unchanged. See PLAN for actual verification results; the live suite extension is not live acceptance evidence until explicitly run.
