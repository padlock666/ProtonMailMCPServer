# Future features

Track requested features here. Inclusion is not authorization to implement them; agree on scope and acceptance criteria before starting. Current implementation progress and verification remain in [PLAN.md](PLAN.md). All changes must preserve [SECURITY.md](SECURITY.md).

## Attachment downloads for AI clients

- **Status:** Design and plan requested on 2026-10-21 and recorded in [ATTACHMENT-DOWNLOADS.md](docs/ATTACHMENT-DOWNLOADS.md). Implementation was subsequently authorized and completed; synthetic evidence and pending client/live acceptance are recorded in PLAN.
- **Goal:** Let a local AI client download an original attachment, especially a PDF, and use its own document tools to read it.
- **Implemented approach:** Optional private-directory configuration enables `download_attachment`; `get_message` supplies attachment IDs. Return the saved path, original filename, declared MIME type, reported/decoded sizes and SHA-256. Preserve all mailbox read-only controls.
- **Storage:** Generated filenames, private files, bounded transfer and directory capacity, no overwrites, explicit retention until operator cleanup. Caller-supplied output paths are excluded.
- **Verification:** Filesystem, real-adapter protocol and MCP process regressions; synthetic client PDF handoff; separately authorized live hash and flag/membership acceptance.
- **Deferred:** Server-side attachment text extraction, OCR, PDF rendering, encrypted-document handling, bulk downloads and remote delivery. The download design serves the earlier attachment-reading goal through the client's own tools.

## Pagination for message searches

- **Status:** Implementation requested and completed on 2026-10-21; synthetic verification tracked in PLAN, live pagination acceptance pending. See [the design](docs/SEARCH-PAGINATION.md) and [implementation checklist](PLAN.md#message-search-pagination--design-requested-2026-10-21).
- **Goal:** Retrieve all matching messages in bounded batches when a search exceeds the current result limit, including through successive AI tool calls.
- **Implemented approach:** Extend `search_messages` with an optional cursor and return `nextCursor` when another page is available. Keep the maximum page size at 50 and the existing descending UID order.
- **Cursor behavior:** Bind an authenticated process-lifetime cursor to the folder, UIDVALIDITY, search filters, and lowest selected candidate UID from the previous page, including vanished candidates. Retrieve matching UIDs below that boundary. Recheck the configured folder policy on every request; reject malformed, mismatched, or stale cursors with sanitized errors.
- **Mailbox changes:** New arrivals must not shift later pages or duplicate earlier results. Messages removed between requests may disappear; pagination does not provide a frozen mailbox snapshot.
- **Verification:** Synthetic adapter protocol and MCP tests for successive pages, final/empty pages, unchanged filters, arrivals/removals, stale UIDVALIDITY, denied folders, invalid cursors, cancellation, limits, and read-only cleanup. Live Bridge verification remains explicit opt-in.
- **Contract:** Repeat folder/filters on continuation; allow page-size changes; preserve `truncated == (nextCursor != null)`. No stored result sets or cross-process cursors. The linked design specifies encoding, validation, empty-page progress, and compatibility.
