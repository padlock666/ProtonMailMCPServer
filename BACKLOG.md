# Future features

Track requested features here. Inclusion is not authorization to implement them; agree on scope and acceptance criteria before starting. Current implementation progress and verification remain in [PLAN.md](PLAN.md). All changes must preserve [SECURITY.md](SECURITY.md).

## Pagination for message searches

- **Status:** Requested; not implemented.
- **Goal:** Retrieve all matching messages in bounded batches when a search exceeds the current result limit, including through successive AI tool calls.
- **Proposed approach:** Extend `search_messages` with an optional cursor and return `nextCursor` when another page is available. Keep the maximum page size at 50 and the existing descending UID order.
- **Cursor behavior:** Bind the cursor to the folder, UIDVALIDITY, search filters, and lowest UID from the previous page. Retrieve matching UIDs below that boundary. Recheck the allowlist on every request; reject malformed, mismatched, or stale cursors with sanitized errors.
- **Mailbox changes:** New arrivals must not shift later pages or duplicate earlier results. Messages removed between requests may disappear; pagination does not provide a frozen mailbox snapshot.
- **Verification needed:** Synthetic adapter protocol and MCP tests for successive pages, final/empty pages, unchanged filters, arrivals/removals, stale UIDVALIDITY, denied folders, invalid cursors, cancellation, limits, and read-only cleanup. Live Bridge verification remains explicit opt-in.
- **Decisions before implementation:** Exact cursor format and validation, request/response schema, and the relationship between `nextCursor` and the existing `truncated` field.
