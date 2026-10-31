# Linux Secret Service implementation — 2026-10-31

`PasswordKeyring` is implemented as an explicit alternative to the legacy protected `PasswordFile`. The application now performs collection-scoped libsecret lookup after pinned TLS, validates exactly one distinct matching item, and supplies a disposable password lease to the existing single-attempt authentication guard. The three base MCP tools, optional download tool, schemas, folder policy, UID identity, EXAMINE/PEEK, TLS pins and no-mutation mailbox behavior are unchanged.

The provider uses direct asynchronous C# P/Invoke with an operation-owned GLib context/thread and actual GCancellable propagation. It subscribes before service resolution, latches owner transitions (including loss/reacquisition and away-and-back), orders notifications with a same-connection bus round trip, drains callbacks and rejects invalidated results. The provider uses libsecret session negotiation and performs no credential commands or automatic retry. Credential acceptance occurs at the final context token/latch check before native cleanup; subsequent changes do not revoke the lease. Native objects and the executable's GIO bus connection are released before lookup completes, and the lease drops its string reference immediately after authentication. The complete [ordering and limits](docs/KEYRING.md#implemented-context-and-acceptance-ordering) are part of the contract.

Source exclusivity and selectors are validated at startup, without lookup. File mode preserves its startup snapshot and works without a bus or usable libsecret. Keyring mode rejects native service/debug overrides before loading, cannot unlock/prompt/mutate/fall back, validates native length before copying at most 4,096 UTF-8 bytes, clears owned byte buffers, and maps only fixed sanitized errors. Guides include a version-tested operator provisioning example; the integration fixture executes it only on synthetic isolated storage. The live fixture loader/observer now handles the selected source, but no live suite was run.

## Versions and dependencies

Environment: Zorin OS 18 / Ubuntu noble base, x86_64; SDK **10.0.114**, runtime **10.0.14**, VSTest **18.0.2**. Native packages queried with `dpkg-query -W libsecret-1-0 libglib2.0-0t64 dbus gnome-keyring python3-dbus python3-gi` and `dpkg-query -W gir1.2-secret-1`:

| Dependency | Tested version |
| --- | --- |
| libsecret runtime / Secret 1 typelib | 0.21.4-1build3 |
| GLib / GIO / GObject | 2.80.0-6ubuntu3.10 |
| D-Bus | 1.14.10-4ubuntu4.1 |
| GNOME Keyring | 46.1-2ubuntu0.2 |
| Python D-Bus / PyGObject | 1.3.2-5build3 / 3.48.2-1 |
| MCP / Core | 2.2.0 |
| Hosting / PKCS | 10.0.13 |
| MailKit / MimeKit | 4.18.1 |
| Test SDK / xUnit / adapter | 18.11.0 / 2.9.3 / 4.0.1 |

No managed binding/package was added; both NuGet lock files remain unchanged. System sonames are `libsecret-1.so.0`, `libgio-2.0.so.0`, `libgobject-2.0.so.0` and `libglib-2.0.so.0`. Headers and GLib/libsecret source were reviewed for ABI, transfer ownership, all-match flags, callbacks, session negotiation and cancellation; no native source/binary is vendored. Libsecret/GLib are LGPL-2.1-or-later. Native development headers were not required or installed. The actual real-GNOME suite observed encrypted `dh-ietf1024-sha256-aes128-cbc-pkcs7`; the fake service deliberately exercises libsecret's supported `plain` fallback. No confidentiality from the trusted local bus/same-user processes is claimed.

## GNOME Keyring setup documentation — 2026-10-31

Added an operator walkthrough in `docs/LINUX-SETUP.md`, linked from README, configuration, usage and the provisioning recipe. It covers Ubuntu 24.04 package names, preparing/unlocking the default GNOME collection, securely entering the Bridge IMAP password, using the generated UUID with the complete `examples/config.keyring.example.json`, restarting the host, fixed-error troubleshooting and rotation. File-mode examples remain valid; keyring mode requires no password file.

Verification: `python3 /tmp/check-gnome-keyring-setup.py` passed (valid complete JSON/selector, unchanged unrelated defaults, five setup steps, four shell blocks checked by `bash -n`, provisioning recipe identical to the previously tested fixture copy); `python3 /tmp/check-keyring-docs-final.py` passed (17 Markdown files, 316 local links/anchors, 24 JSON blocks, balanced fences and compiled Python); `git diff --check` passed. GNOME's official UI instructions and Ubuntu package metadata were reviewed. `dpkg-query -W gnome-keyring libsecret-1-0 python3-gi gir1.2-secret-1 seahorse` read installed package metadata only; Seahorse is 43.0-3build2 and the other package versions match the table above.

Documentation/examples only: no application/test code changed, no build or application/integration tests reran, and no personal keyring, Bridge or mailbox was accessed. No packages were installed. GUI steps were verified against official documentation rather than executed on the personal desktop; existing isolated recipe evidence and unverified live acceptance are unchanged.

## Documentation and test cleanup — 2026-10-31

The keyring guides now describe the current contract, native requirements and provider acceptance coverage. The test fixture and its four GNOME provider tests share an independent authorization gate. Production code, managed package pins and lock files are unchanged; the application DLL retains SHA-256 `9b208a3968a2ba4e103e6bd086a4fa384e57396e357912ee11c9753c620a0767`.

| Exact command/check | Result |
| --- | --- |
| `dotnet restore ProtonMailMcp.slnx --locked-mode` | Passed; dependency graph unchanged. |
| `dotnet build ProtonMailMcp.slnx --configuration Release --no-restore` | Passed, 0 warnings/errors. |
| `dotnet test ProtonMailMcp.slnx --configuration Release --no-build --filter 'Category=Unit\|FullyQualifiedName~KeyringPasswordTests\|FullyQualifiedName~KeyringProcessTests' --logger 'trx;LogFileName=keyring-cleanup-focused.trx' --results-directory /tmp/protonmcp-keyring-cleanup-results` | **273 passed, 0 failed, 0 skipped**, 1m 5s. Includes category enforcement and all retained native/process keyring tests. The filter contains literal pipes without Markdown table escapes. |
| `PROTONMCP_KEYRING_INTEGRATION=I_AUTHORIZE_ISOLATED_SYNTHETIC_KEYRING_TESTS dotnet test ProtonMailMcp.slnx --configuration Release --no-build --filter 'Category=KeyringIntegration' --logger 'trx;LogFileName=keyring-cleanup-gnome.trx' --results-directory /tmp/protonmcp-keyring-cleanup-results` | **4 passed, 0 failed, 0 skipped**, 2 seconds, using only isolated synthetic GNOME storage and owned processes. |
| `dotnet test ProtonMailMcp.slnx --configuration Release --no-build --filter 'Category=KeyringIntegration' --logger 'trx;LogFileName=keyring-cleanup-gate.trx' --results-directory /tmp/protonmcp-keyring-cleanup-results` | **0 passed, 0 failed, 4 skipped** without the gate; no fixture access. This is gate verification, not compatibility evidence. |
| `python3 /tmp/check-keyring-docs-final.py`; `git diff --check` | Passed: 17 Markdown files, 303 local links/anchors, 23 JSON blocks, balanced fences and compiled provisioning recipe. |

Full standard and published-artifact suites were not rerun for this cleanup; their implementation-time evidence below remains historical. Live Bridge and personal-keyring access were neither authorized nor performed.

## Implementation verification — before documentation/test cleanup

All commands ran from the repository root. .NET compiler/VSTest IPC, package audit/network and fixture-owned Unix/loopback sockets required execution outside the restricted sandbox. The initial restricted build exited 1 with no diagnostic errors; the authorized retry succeeded. No audit controls, live gates or TLS controls were disabled.

| Exact command/check | Actual result |
| --- | --- |
| `dotnet restore ProtonMailMcp.slnx --locked-mode` | Passed; existing cached graph up to date, NuGet audit enabled. |
| `dotnet build ProtonMailMcp.slnx --configuration Release --no-restore` | Passed, 0 warnings/errors. |
| `dotnet test ProtonMailMcp.slnx --configuration Release --no-build --filter 'Category!=LiveBridge&Category!=KeyringIntegration' --logger 'trx;LogFileName=keyring-standard-verified.trx' --results-directory /tmp/protonmcp-keyring-results` | **482 passed, 0 failed, 0 skipped**, 5m 34s. |
| `dotnet publish src/ProtonMailMcp/ProtonMailMcp.csproj --configuration Release --no-build --no-restore --output /tmp/protonmcp-keyring-verified-publish` | Passed; local framework-dependent artifact. |
| `PROTONMCP_TEST_APPLICATION=/tmp/protonmcp-keyring-verified-publish/ProtonMailMcp.dll dotnet test ProtonMailMcp.slnx --configuration Release --no-build --filter 'Category=Protocol\|Category=Mcp' --logger 'trx;LogFileName=keyring-published-verified.trx' --results-directory /tmp/protonmcp-keyring-results` | **266 passed, 0 failed, 0 skipped**, 5m 34s. The shell argument contains a literal pipe without the Markdown table escape. Child-process tests use the published DLL; in-process protocol tests use the matching Release DLL. |
| `PROTONMCP_KEYRING_INTEGRATION=I_AUTHORIZE_ISOLATED_SYNTHETIC_KEYRING_TESTS dotnet test ProtonMailMcp.slnx --configuration Release --no-build --filter 'Category=KeyringIntegration' --logger 'trx;LogFileName=keyring-gnome-accepted.trx' --results-directory /tmp/protonmcp-keyring-results` | **5 passed, 0 failed, 0 skipped**, 2 seconds. Historical implementation result; current GNOME coverage and checks are recorded below. |
| Same category with no integration gate, `--logger 'trx;LogFileName=keyring-gate-final.trx' --results-directory /tmp/protonmcp-keyring-results` | **0 passed, 0 failed, 5 skipped** before fixture launch. Gate evidence only, not compatibility. |
| `dotnet list ProtonMailMcp.slnx package --vulnerable --include-transitive --no-restore` | NuGet reported no known vulnerable packages for either project. This does not audit the OS/runtime or predict future advisories. |
| `sha256sum src/ProtonMailMcp/bin/Release/net10.0/ProtonMailMcp.dll /tmp/protonmcp-keyring-verified-publish/ProtonMailMcp.dll` | Both: `9b208a3968a2ba4e103e6bd086a4fa384e57396e357912ee11c9753c620a0767`. |
| `python3 /tmp/check-keyring-docs-final.py`; `git diff --check` | Passed: 17 Markdown files, 310 local links/anchors, 23 JSON blocks, balanced fences, compiled Python recipe; whitespace check passed. |
| Live Bridge / personal desktop keyring / intended-client PDF interpretation | **Not run.** Live access and personal-keyring access were not authorized. No plugin/package installation, credential discovery or Bridge-store inspection occurred. |

For an unescaped copyable artifact command:

```bash
PROTONMCP_TEST_APPLICATION=/tmp/protonmcp-keyring-verified-publish/ProtonMailMcp.dll \
dotnet test ProtonMailMcp.slnx --configuration Release --no-build \
  --filter 'Category=Protocol|Category=Mcp' \
  --logger 'trx;LogFileName=keyring-published-verified.trx' \
  --results-directory /tmp/protonmcp-keyring-results
```

## Remaining limits and next actions

Live keyring-backed Bridge authentication, actual Bridge read-only flag/membership preservation and personal desktop-client launch remain unverified. They need separate explicit authorization and operator-prepared external settings/fixtures; the MVP remains incomplete. Intended-client PDF interpretation and other pre-existing Phase 4 gaps remain open.

The trusted desktop bus/service and same-user/root/launcher limits remain. This monitoring rule authenticates neither GNOME nor individual internal metadata requests. Post-acceptance transitions do not revoke the credential. Libsecret/GDBus allocate search/metadata replies before application limits; cleanup releases all results, so neither constant memory nor hard real-time cleanup is promised. Managed strings and MailKit/native internal copies cannot be reliably erased. The executable closes its GIO bus connection per lookup and currently has no other GIO consumers; future independent consumers require an ownership review. No non-GNOME, headless-unlock, alternate architecture, privileged-owner, native allocator-failure or arbitrary process-kill guarantee is inferred from these tests.


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
