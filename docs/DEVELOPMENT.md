# Development and handoff guide

Phase 1 is implemented. The build commands and external configuration below are operational; mailbox operations, MCP tools, and Bridge handoff remain later-phase work.

## Working with Codex

Open the repository root in VS Code. Begin with [AGENTS.md](../AGENTS.md); it directs Codex to [PLAN.md](../PLAN.md), [DESIGN.md](../DESIGN.md), [SECURITY.md](../SECURITY.md), and the [testing guide](TESTING.md). Keep root instructions short and put detailed behavior in those linked documents.

Codex discovers repository `AGENTS.md` instructions; arbitrary design and plan filenames are not a replacement for that entry point. The links and explicit reading instructions make the remaining documents part of the workflow. If instructions appear stale after changing `AGENTS.md`, start a fresh Codex session. See the [official instruction guide](https://developers.openai.com/codex/guides/agents-md).

Use task prompts with a phase, intended result, constraints, and required evidence. Do not paste secrets or personal email into prompts. Do not create extra agent configuration, skills, or nested instruction files unless a concrete need emerges.

Completed Phase 1 request:

> Implement Phase 1 in PLAN.md. Read AGENTS.md and the linked documents first. Establish the .NET 10 solution, configuration validation, stdio host, and offline test foundation. Preserve the approved scope and security boundaries. Do not connect to my Bridge or mailbox. Run the relevant checks, update the phase checklist and evidence ledger, and report what remains.

Example continuation:

> Implement Phase 2 in PLAN.md using synthetic protocol fixtures. Verify the actual MailKit adapter uses read-only folder selection and PEEK, including failure and cleanup paths. Update the acceptance evidence without adding MCP features or contacting a live mailbox.

A request for one phase authorizes that phase; a request for the full implementation can authorize all planned implementation work. Live mailbox access remains a deliberate opt-in with operator-supplied test configuration, as specified in the testing guide.

## Model and reasoning effort

Project recommendation as of 2026-10-17: use **GPT-6.1 Sol** for implementation, with **Medium** reasoning effort for routine work and **High** for the IMAP safety boundary. OpenAI positions this model as a balance of coding capability and cost; the phase-specific choices below are project recommendations, not measured performance guarantees. See the [model description](https://developers.openai.com/api/docs/models/gpt-6.1-sol) and [model-selection guidance](https://developers.openai.com/api/docs/guides/model-selection).

| Work | Recommended effort | Reason |
| --- | --- | --- |
| Phase 1 — Foundation | Medium | Establish the solution, configuration validation, hosting, and offline test foundation |
| Phase 2 — Read-only adapter | High | Reason carefully about IMAP commands, TLS, UID identity, cancellation, transfer limits, and cleanup |
| Phase 3 — MCP interface | Medium | Connect defined tool contracts to the tested adapter and verify process behavior |
| Phase 4 — Bridge validation and handoff | Medium; High for safety failures | Follow the acceptance procedure; increase effort for unexpected flag changes, TLS issues, or recovery behavior |
| Final code and safety review | High | Check read-only guarantees across success, failure, cancellation, and shutdown paths |

Reserve **Extra high** for a difficult unresolved adapter issue or security question where deeper investigation is useful. Do not make it the default for routine work; higher effort adds latency and cost without guaranteeing better results. See [OpenAI's reasoning-effort guidance](https://developers.openai.com/api/docs/guides/deployment-checklist).

These settings apply to the coding assistant used to develop the server. Select the model and effort available in your Codex client; the application itself does not call a model API. Revisit the recommendation if model availability changes. Model choice does not replace the required protocol tests, live acceptance evidence, or code review.

Proceed one implementation phase at a time; Phase 2 is next after review of Phase 1 evidence. Review each phase's changes and acceptance evidence in `PLAN.md` before beginning the next phase. Keep live Bridge validation in Phase 4, after the protocol tests pass.

## Toolchain and layout

Use .NET 10, VS Code C# tooling, Generic Host, the official `ModelContextProtocol` package, MailKit/MimeKit, and xUnit. Use one root `ProtonMailMcp.slnx`, `src/ProtonMailMcp`, and `tests/ProtonMailMcp.Tests`. Do not add a web host, database, container, service installer, or extra projects for the MVP.

For dependency/toolchain maintenance:

1. Verify the available .NET 10 SDK, choose a concrete SDK pin/roll-forward policy, and document it in the created `global.json`.
2. Verify compatible official package APIs and choose exact versions; no floating package versions. MailKit brings MimeKit transitively; add a direct MimeKit reference only when appropriate for explicit API usage and keep versions compatible.
3. Generate and commit NuGet lock files. Establish normal locked restore and intentional lock-update workflow.
4. Add sensible .NET generated-output and local-secret exclusions before producing local files. Ignore rules are a convenience, not permission to store real mail or credentials in the repository.
5. Enable nullable reference types and use asynchronous, cancellable I/O throughout.

## Build and test workflow

Run the following commands from the repository root:

```bash
dotnet restore ProtonMailMcp.slnx --locked-mode
dotnet build ProtonMailMcp.slnx --configuration Release --no-restore
dotnet test ProtonMailMcp.slnx --configuration Release --no-build --filter 'Category!=LiveBridge'
```

Offline here means no Proton/Bridge account access; initial NuGet restore may need package-network access. Do not claim an offline package restore without a populated cache.

For focused verification, select `Unit` or `Mcp`. `Protocol` tests arrive in Phase 2. Phase 4 defines an additional explicit opt-in for `LiveBridge`; live tests never run as part of the ordinary test command. Record checks actually run, failures, and environmental limits in `PLAN.md`.

## Pinned dependencies and verification sources

`global.json` pins SDK **10.0.112**, disables roll-forward, and excludes prereleases. Both projects target `net10.0` with nullable types and warnings as errors. Select a different SDK only by an intentional pin update.

| Dependency | Exact version | Rationale |
| --- | --- | --- |
| ModelContextProtocol / Core | 2.2.0 | Official stdio/Generic Host SDK; net10.0 assets and APIs verified from restored XML docs, package metadata, and process tests |
| Microsoft.Extensions.Hosting | 10.0.13 | Generic Host without ASP.NET hosting; explicit defaults disabled |
| MailKit / transitive MimeKit | 4.18.1 / 4.18.1 | Compatible pinned mail stack, reserved for Phase 2; no adapter or network calls yet |
| System.Security.Cryptography.Pkcs | 10.0.13 | Direct override of MimeKit's vulnerable 10.0.0 minimum |
| Microsoft.NET.Test.Sdk | 18.11.0 | VSTest runner for net10.0 |
| xUnit / Visual Studio adapter | 2.9.3 / 4.0.1 | xUnit v2 tests and compatible adapter |

Each project has a `packages.lock.json` fixing the complete graph and hashes. NuGet.org is the explicit package source. Publication dates, target frameworks, dependencies, and package release metadata were reviewed before pinning; successful restore/build/process tests establish Phase 1 API compatibility. The [SDK getting-started guide](https://github.com/modelcontextprotocol/csharp-sdk/blob/6fa3825973949a9c4f0cd8af344e15a8db09dc35/docs/concepts/getting-started.md) is a reference; this implementation uses no assembly tool discovery. The PKCS override addresses [Microsoft's October advisory](https://github.com/advisories/GHSA-846f-f764-76mx) and [September advisory](https://github.com/advisories/GHSA-2fmv-rv2c-59x7). NuGet auditing remains enabled for the entire graph, with warnings treated as errors.

For an intentional dependency update, edit exact package references, run `dotnet restore ProtonMailMcp.slnx --force-evaluate`, review both lock files and security advisories, then run the locked restore/build/test commands. MailKit upgrades require protocol regression once the adapter exists. Check SDK/runtime security updates independently; package audit does not audit the installed runtime.

In this execution environment, restore used `--packages /tmp/protonmcp-packages` and commands used `DOTNET_CLI_HOME=/tmp/protonmcp-dotnet` to keep cache and first-run state in writable temporary locations. Neither is required in a normal developer shell. Network access was needed for NuGet; no offline-cache-only restore is claimed.

## External configuration

Supply `--config /absolute/path/to/config.json` with a configuration file outside the repository, preferably in a private XDG configuration directory. All referenced file paths must also be absolute. The application reads only that JSON file; appsettings, environment variables, and extra command-line overrides are not configuration sources. Operators must keep all real configuration and secrets outside the checkout; runtime does not infer repository boundaries.

The approved settings are:

| Setting | Meaning |
| --- | --- |
| Host | Literal loopback IP; default `127.0.0.1` |
| IMAP port | Explicit value copied from Bridge settings |
| TLS mode | Explicit required STARTTLS or TLS-on-connect, matching Bridge |
| Username | Bridge IMAP username |
| Password-file path | Separate external owner-only file with Bridge-generated password |
| Public-certificate path | Exported Bridge certificate; never its private key |
| Allowed folders | Explicit nonempty full-name list; setup starts with `INBOX` |

Do not infer TLS mode from a port or assume Bridge uses default ports. Use Bridge's displayed settings. The case-sensitive keys and accepted values are shown below; unknown keys fail startup.

Example JSON uses placeholders only and is not an instruction to configure a live mailbox during Phase 1:

```json
{
  "Host": "127.0.0.1",
  "Port": 1143,
  "TlsMode": "StartTls",
  "Username": "<bridge-imap-username>",
  "PasswordFile": "/absolute/private/directory/bridge.password",
  "CertificateFile": "/absolute/private/directory/bridge-public.pem",
  "AllowedFolders": ["INBOX"]
}
```

Use the port and TLS mode displayed by Bridge; `1143` is illustrative. Accepted modes are exactly `StartTls` (mandatory STARTTLS) and `SslOnConnect`. IPv4 must use canonical dotted decimal; IPv6 literal loopback is also supported. Hostnames, scoped addresses, and alternate IPv4 spellings fail. `Host` alone may be omitted to use `127.0.0.1`. All other settings are required. Allowlists support up to 256 entries of at most 1,024 characters each and match exact folder names, with case-insensitive `INBOX` only; wildcards, blank entries, and control characters fail.

The public certificate must be a single PEM certificate or DER X.509 certificate, within its validity period. Private-key bundles and PKCS#12 are rejected. Configuration/certificate files are capped at 64 KiB each. The UTF-8 password file is capped at 4 KiB, accepts one terminal LF/CRLF, preserves spaces, and rejects embedded control characters or an empty password.

On Linux the password file must be regular, readable, owned by the effective user, and have no group/other or special permission bits (typically `0600` or `0400`). Its immediate containing directory must also be user-owned and private (normally `0700`). `open`/`openat` use `O_NOFOLLOW`, `O_NONBLOCK`, and `O_CLOEXEC`; `statx(AT_EMPTY_PATH)` validates the actual opened handles before reading. Final-component symlinks for the file and its immediate parent are rejected. Keep ancestor directories trusted. Linux `statx` support is required; failure is closed. Startup never changes file permissions.

Launch the built artifact so build output cannot enter MCP stdout:

```bash
dotnet src/ProtonMailMcp/bin/Release/net10.0/ProtonMailMcp.dll --config /absolute/path/to/config.json
```

A parent MCP client normally owns stdin/stdout; this is not an interactive mail command. Phase 1 supports initialization only and advertises no tools, resources, or prompts. It never instantiates an IMAP client. The validated certificate bytes are retained for Phase 2 pin enforcement; local certificate loading alone does not establish network TLS safety.

Startup configuration errors exit `2` with a fixed `startup: configuration_...` stderr diagnostic. Other host failures exit `1` with `host: failed`; normal shutdown exits `0`. Framework/SDK logs are filtered off, the console provider targets stderr, and application diagnostics use fixed codes without raw exceptions. Environment logging overrides cannot enable payload logs. Future operation logging must retain the same sanitization boundary.

The password file must meet the ownership/permission requirements in `SECURITY.md`. Credentials do not belong in launch arguments, checked-in VS Code files, examples, logs, or test fixtures. The server never needs the Proton account password.

## Later VS Code and client setup

Phase 4 will provide build/debug tasks and complete MCP client setup against the implemented mailbox tools. Any launch example must use placeholders and external configuration paths. Do not check in personal paths, usernames, or credentials.

Debug using a synthetic IMAP fixture by default. Launch the built executable for MCP use so build banners do not enter stdout. Configure logging to stderr and inspect debugger/log output for accidental content leakage before testing real mail.

The server process has no HTTP listener or MCP OAuth flow in this design. The parent client decides which model receives tool results. Document that client-side privacy decision when connecting real mail.

## Handoff and maintenance

At the end of each implementation task:

- Update only completed acceptance checkboxes, and record actual verification evidence in `PLAN.md`.
- Update design/interface documentation if implementation exposes a necessary clarification; raise safety/scope changes before implementing them.
- State pending work and limitations clearly, especially unrun live tests.
- Review changed files for credentials, real mail, unbounded reads, stdout contamination, and read/write IMAP paths.

For dependency upgrades, inspect release notes/API changes and intentionally refresh pins/lock files. Re-run the protocol safety suite for MailKit changes. Record a tested Bridge version only after live acceptance; do not equate latest documentation with tested compatibility.
