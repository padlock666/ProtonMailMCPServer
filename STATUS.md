# Independent review — 2026-10-30

Release/MVP acceptance is **not satisfied**. The final offline and published-artifact suites passed. Real Bridge acceptance and intended-client PDF interpretation have not been run. Existing operator configuration, credentials and mailbox data were not accessed.

## Scope and applicability

Reviewed the repository instructions, solution, application, tests, dependency locks and linked behavior/security documents independently of prior completion claims. The checkout contains one .NET 10 console application and one xUnit project, with three base MCP tools and the explicitly configured attachment-download tool. It has no OpenAPI specification, HTTP server/routes/origins/redirects, OAuth refresh tokens, invoices, monetary values or balances. Those requested checks are not applicable to this implementation; no OpenAPI agreement is claimed. The actual contract is the MCP schema/DTO boundary in [MailboxTool.cs](src/ProtonMailMcp/Tools/MailboxTool.cs) and [USAGE.md](docs/USAGE.md).

Pre-existing working-tree documentation edits were preserved. No package versions, lock files, mailbox capabilities or safety boundaries were broadened.

## Confirmed findings fixed

| Severity | Defect and impact | Fix and regression evidence |
| --- | --- | --- |
| Medium | Text fetches lacked attachment-style payload binding. Partial-offset/duplicate literals reached the successful cleanup path; wrong UIDs/sections were misreported as missing messages. Duplicate allocations could multiply the intended per-fetch allowance. | [BoundedImapClient.cs](src/ProtonMailMcp/Mail/BoundedImapClient.cs) now permits one fixed-capacity literal for the expected UID/section and zero offset. [MailKitMailbox.cs](src/ProtonMailMcp/Mail/MailKitMailbox.cs) rejects discarded unexpected literals. Five failing-before protocol cases cover offsets, duplicates, UID before/after the literal and wrong sections; a positive case permits the correct UID after a literal. Recovery uses a fresh connection. |
| Medium | Authentication could retry credentials inside one connection despite the no-retry contract. The prior fixture advertised no SASL, so it missed mechanism fallback. | A per-connection credential guard in [MailKitMailbox.cs](src/ProtonMailMcp/Mail/MailKitMailbox.cs) prevents a second credential request while retaining MailKit's initial selection. Two failing-before fixtures observed `AUTHENTICATE LOGIN` or `LOGIN` after PLAIN rejection. Rejection/recovery and successful SASL over both TLS modes now pass. The behavior was cross-checked against [pinned MailKit async source](https://raw.githubusercontent.com/jstedfast/MailKit/4.18.1/MailKit/Net/Imap/AsyncImapClient.cs). |
| Medium | Text decoding silently accepted malformed transfer bytes as `available`, contrary to its explicit malformed-content contract. This could present repaired or unreliable text as a successful read. | [MimeText.cs](src/ProtonMailMcp/Mail/MimeText.cs) now shares the strict attachment transfer decoder. Five failing-before inputs cover nonzero base64 padding bits, invalid quoted-printable bytes/soft breaks and high bytes declared as 7bit. Valid encodings, charset handling, size limits and cancellation remain covered; a real-adapter case returns metadata with `malformed` and no text. |
| Low | Documentation linked to a missing historical archive, making original authorizations/checklists/evidence inaccessible. | Recovered [IMPLEMENTATION-HISTORY.md](docs/archive/IMPLEMENTATION-HISTORY.md) from `PLAN.md` at commit `fd478e5`, identified as historical rather than fresh verification; preserved its content with relocated links and an attachment-sequence anchor. |

The raw process test harness now waits after killing its own child process tree, and all MCP process tests can target the published DLL using the test-only `PROTONMCP_TEST_APPLICATION` setting. The production configuration interface is unchanged.

## Verification evidence

Environment: Linux (Zorin 18, x64), SDK **10.0.113**, runtime **10.0.13**. Package pins: MCP **2.2.0**, Hosting/PKCS **10.0.13**, MailKit/MimeKit **4.18.1**, Test SDK **18.11.0**, xUnit **2.9.3**, adapter **4.0.1**. Both lock files are tracked and unchanged.

| Command/check | Actual result |
| --- | --- |
| `dotnet restore ProtonMailMcp.slnx --locked-mode` | Passed outside the sandbox with NuGet audit enabled. Initial restricted attempt exited 1 without diagnostics; no success was inferred from that attempt. |
| `dotnet build ProtonMailMcp.slnx --configuration Release --no-restore` | Passed after the changes, 0 warnings/errors. |
| Initial complete offline suite, `Category!=LiveBridge` | 379 passed, 0 failed/skipped, 5m 34s. This initial run overlapped subsequent builds, so it is not an isolated pre-fix application baseline; final verification uses the stable final binaries. |
| Defect reproductions | Text/literal run: 10 failed, 5 passed before fixes. SASL run: 2 failed before the guard, with actual extra authentication commands. |
| Focused MIME/text/attachment regressions | 118 passed, 0 failed/skipped after body fixes. |
| Focused SASL rejection/success regressions | 4 passed, 0 failed/skipped after the credential guard. |
| Final complete offline suite, `Category!=LiveBridge` | **398 passed, 0 failed, 0 skipped**, 5m 35s, against stable final binaries. |
| `dotnet publish src/ProtonMailMcp/ProtonMailMcp.csproj --configuration Release --no-build --no-restore --output /tmp/protonmcp-review-publish` | Passed; local framework-dependent artifact created. Published DLL SHA-256 matches the Release DLL (`a95a3dd6329dd1f4b8ce3708fe28b6623b2a980c61fae53453c027da18f11250`). |
| Published-DLL process suite, `Category=Mcp` | **69 passed, 0 failed, 0 skipped**, 5m 35s. Covers initialization/discovery, schemas and annotations, actual synthetic tool traffic/download bytes, sanitized errors/stderr, cancellation/recovery and EOF/child shutdown. |
| `dotnet list ProtonMailMcp.slnx package --vulnerable --include-transitive --no-restore` | NuGet reported no known vulnerable packages in either project using its configured source. This does not audit the installed OS/runtime or establish future vulnerability status. |
| Documentation checks / `git diff --check` | Passed: 16 Markdown files, 278 local links/anchors, 21 JSON blocks; three pre-existing PLAN navigation anchors corrected. Rechecked after final test results were recorded. Archive content also matches `fd478e5:PLAN.md` after reversing only the documented editorial changes. |
| Real Bridge acceptance / intended-client PDF interpretation | Not run; neither inferred from synthetic success nor marked complete. |

Final test commands use `--configuration Release --no-build`, with `--filter 'Category!=LiveBridge'` for the whole offline suite and `--filter 'Category=Mcp'` with `PROTONMCP_TEST_APPLICATION=/tmp/protonmcp-review-publish/ProtonMailMcp.dll` for the artifact. TRX evidence is in `/tmp/protonmcp-review-results` (`review-baseline`, `review-reproductions`, `review-focused`, `review-auth-reproduction`, `review-auth-fixed`, `review-final`, `review-published`). Temporary artifacts are local, not committed. Network/package cache and synthetic loopback/IPC permissions were used; no audit controls or live gates were disabled.

## Remaining findings and evidence limits

| Severity/status | Reference and impact | Next action |
| --- | --- | --- |
| Medium, accepted resource limitation | [MailKitMailbox.cs](src/ProtonMailMcp/Mail/MailKitMailbox.cs), `SearchAsync` materializes the matching UID set before limiting summaries; MailKit parses namespace/envelope/BODYSTRUCTURE metadata before application output limits. Large responses can consume substantially more memory than the returned page. This existing limitation is documented in [DESIGN.md](DESIGN.md); no constant-memory or hard real-time guarantee is proven. | Retain the documented limit and narrow folder/query scope operationally. Any streaming/search architecture change needs separate scope approval. |
| Release blocker: unverified compatibility | [PLAN.md](PLAN.md#phase-4--bridge-validation-and-handoff) and [TESTING.md](docs/TESTING.md#opt-in-real-bridge-acceptance): synthetic IMAP proves emitted commands, not actual Bridge flag/membership preservation, date matching, recovery, certificate replacement or label behavior. Tightened MIME/authentication handling has no live regression evidence. | Operator prepares external fixtures/configuration and explicitly authorizes the relevant live stages. Record exact Bridge/Linux/client versions and unsupported states. |
| Acceptance gap: client/file and filesystem failures | [TESTING.md](docs/TESTING.md#environment-and-evidence-limits): SDK path/byte handoff is not intended-client PDF interpretation; native disk-full/flush faults, privileged ownership and forced-kill publication timing are not proven. | Verify the intended client's synthetic PDF access; retain the documented filesystem/crash limitations and complete applicable acceptance checks before release. |

No critical/high defect was confirmed in the reviewed paths. Output bounds do not imply boundless hostile-input resistance. No HTTP or financial correctness conclusion is applicable to this repository.

## Next action

Obtain the separately required operator authorization and prepared external fixtures for live acceptance under [TESTING.md](docs/TESTING.md), including the tightened authentication and body-validation paths. Verify intended-client synthetic PDF access separately. Record outcomes and versions; release criteria remain unsatisfied until the outstanding acceptance checks have evidence.
