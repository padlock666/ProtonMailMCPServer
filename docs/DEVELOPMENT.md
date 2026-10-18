# Development and handoff guide

Phases 1–3 are implemented. The build commands, external configuration, and internal read-only adapter are operational with synthetic protocol evidence. The three MCP tools have synthetic process evidence; Phase 4 tooling and handoff documentation are implemented, with a successful operator-performed real Bridge folder-listing and date-range search smoke test recorded on 2026-10-18. Full live acceptance remains pending; see [PLAN.md](../PLAN.md). That test used the former internal-date filter; sender-date filtering now replaces it and awaits live verification. See [usage](USAGE.md) for the updated inputs.

## Working with Codex

Open the repository root in VS Code. Begin with [AGENTS.md](../AGENTS.md); it directs Codex to [PLAN.md](../PLAN.md), [DESIGN.md](../DESIGN.md), [SECURITY.md](../SECURITY.md), and the [testing guide](TESTING.md). Keep root instructions short and put detailed behavior in those linked documents.

Codex discovers repository `AGENTS.md` instructions; arbitrary design and plan filenames are not a replacement for that entry point. The links and explicit reading instructions make the remaining documents part of the workflow. If instructions appear stale after changing `AGENTS.md`, start a fresh Codex session. See the [official instruction guide](https://developers.openai.com/codex/guides/agents-md).

Use task prompts with a phase, intended result, constraints, and required evidence. Do not paste secrets or personal email into prompts. Do not create extra agent configuration, skills, or nested instruction files unless a concrete need emerges.

Completed Phase 1 request:

> Implement Phase 1 in PLAN.md. Read AGENTS.md and the linked documents first. Establish the .NET 10 solution, configuration validation, stdio host, and offline test foundation. Preserve the approved scope and security boundaries. Do not connect to my Bridge or mailbox. Run the relevant checks, update the phase checklist and evidence ledger, and report what remains.

Completed Phase 2 request:

> Implement Phase 2 in PLAN.md using synthetic protocol fixtures. Verify the actual MailKit adapter uses read-only folder selection and PEEK, including failure and cleanup paths. Update the acceptance evidence without adding MCP features or contacting a live mailbox.

Completed Phase 3 request:

> Implement Phase 3 in PLAN.md using the tested adapter and synthetic fixtures. Register only the three approved MCP tools, verify them through the official SDK client, and update acceptance evidence. Do not contact a live mailbox.

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

Proceed one implementation phase at a time; Phase 4 live acceptance remains pending explicit authorization and configuration. Review each phase's changes and acceptance evidence in `PLAN.md` before beginning the next phase. Keep live Bridge validation in Phase 4, after the protocol tests pass.

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

For focused verification, select `Unit`, `Protocol`, or `Mcp`. `Protocol` exercises the actual adapter against synthetic TLS/IMAP sessions. Phase 4 implements an additional explicit opt-in for `LiveBridge`; live tests never run as part of the ordinary test command. Record checks actually run, failures, and environmental limits in `PLAN.md`.

## Pinned dependencies and verification sources

`global.json` pins SDK **10.0.112**, disables roll-forward, and excludes prereleases. Both projects target `net10.0` with nullable types and warnings as errors. Select a different SDK only by an intentional pin update.

| Dependency | Exact version | Rationale |
| --- | --- | --- |
| ModelContextProtocol / Core | 2.2.0 | Official stdio/Generic Host SDK; net10.0 assets and APIs verified from restored XML docs, package metadata, and process tests |
| Microsoft.Extensions.Hosting | 10.0.13 | Generic Host without ASP.NET hosting; explicit defaults disabled |
| MailKit / transitive MimeKit | 4.18.1 / 4.18.1 | Pinned mail stack verified by the Phase 2 synthetic protocol suite |
| System.Security.Cryptography.Pkcs | 10.0.13 | Direct override of MimeKit's vulnerable 10.0.0 minimum |
| Microsoft.NET.Test.Sdk | 18.11.0 | VSTest runner for net10.0 |
| xUnit / Visual Studio adapter | 2.9.3 / 4.0.1 | xUnit v2 tests and compatible adapter |

Each project has a `packages.lock.json` fixing the complete graph and hashes. NuGet.org is the explicit package source. Publication dates, target frameworks, dependencies, and package release metadata were reviewed before pinning; successful restore/build/process tests establish Phase 1 API compatibility. The [SDK getting-started guide](https://github.com/modelcontextprotocol/csharp-sdk/blob/6fa3825973949a9c4f0cd8af344e15a8db09dc35/docs/concepts/getting-started.md) is a reference; this implementation uses no assembly tool discovery. The PKCS override addresses [Microsoft's October advisory](https://github.com/advisories/GHSA-846f-f764-76mx) and [September advisory](https://github.com/advisories/GHSA-2fmv-rv2c-59x7). NuGet auditing remains enabled for the entire graph, with warnings treated as errors.

For an intentional dependency update, edit exact package references, run `dotnet restore ProtonMailMcp.slnx --force-evaluate`, review both lock files and security advisories, then run the locked restore/build/test commands. MailKit upgrades require protocol regression once the adapter exists. Check SDK/runtime security updates independently; package audit does not audit the installed runtime.

In this execution environment, restore used `--packages /tmp/protonmcp-packages` and commands used `DOTNET_CLI_HOME=/tmp/protonmcp-dotnet` to keep cache and first-run state in writable temporary locations. Neither is required in a normal developer shell. Network access was needed for NuGet; no offline-cache-only restore is claimed.

## Configuration and usage documentation

[CONFIGURATION.md](CONFIGURATION.md) is the operator reference for JSON settings, file locations, permissions, allowlists, credentials, TLS pins, and configuration changes. [USAGE.md](USAGE.md) covers building, stdio launch, current capabilities, limits, diagnostics, and live acceptance status. Keep operational instructions in those guides and link to them from the README rather than maintaining duplicate setup instructions here.

When implementation changes affect settings or validation, update `CONFIGURATION.md` and [examples/config.example.json](../examples/config.example.json) in the same task. Keep the example synchronized with the documented JSON and use placeholders only. When they affect launch arguments, client setup, exposed tools/contracts, results, limits, diagnostics, shutdown, or supported workflows, update `USAGE.md`. Review both guides for each implementation phase, including changes to readiness or live evidence, and update their README links/descriptions if needed. Document only implemented behavior as available; label planned interfaces and unverified Bridge compatibility explicitly.

Validate local Markdown links, fenced JSON examples, command paths/options, and documented defaults/limits against the implementation. Use synthetic fixtures for verification; documentation work does not authorize credential discovery or live mailbox access. Record checks actually run and remaining limitations in `PLAN.md`.

## VS Code build, test and debug

Install the Microsoft C# extension and open `ProtonMailMcp.slnx`. Build and run synthetic tests with the commands above. A workspace task can use the following `.vscode/tasks.json`; no task sets live opt-in variables:

```json
{
  "version": "2.0.0",
  "tasks": [
    {
      "label": "build-release", "type": "process", "command": "dotnet",
      "args": ["build", "ProtonMailMcp.slnx", "--configuration", "Release", "--no-restore"],
      "problemMatcher": "$msCompile", "group": "build"
    },
    {
      "label": "test-synthetic", "type": "process", "command": "dotnet",
      "args": ["test", "ProtonMailMcp.slnx", "--configuration", "Release", "--no-build", "--filter", "Category!=LiveBridge"],
      "dependsOn": "build-release", "problemMatcher": "$msCompile", "group": "test"
    }
  ]
}
```

Run locked restore first. Use C# Test Explorer to debug a `Unit`, `Protocol` or `Mcp` test; the synthetic fixtures supply their own isolated settings. For application breakpoints, start the host via a trusted synthetic SDK client and attach the debugger to that child process, leaving stdio owned by the client. An optional `.vscode/launch.json` attach configuration is:

```json
{
  "version": "0.2.0",
  "configurations": [
    {
      "name": "Attach to synthetic MCP host", "type": "coreclr", "request": "attach",
      "processId": "${command:pickProcess}"
    }
  ]
}
```

Select only the known synthetic child process. Breakpoints can trigger the fixed operation deadline; do not change production limits to debug. These examples follow [Microsoft's C# debugger configuration](https://code.visualstudio.com/docs/csharp/debugger-settings); interactive VS Code setup remains unverified. [USAGE.md](USAGE.md#vs-code-mcp-launch) gives the stdio client configuration and [LINUX-SETUP.md](LINUX-SETUP.md) covers Bridge setup. Keep personal paths, usernames, and credentials out of tracked launch files.

Debug using a synthetic IMAP fixture by default. Launch the built executable for MCP use so build banners do not enter stdout. Configure logging to stderr and inspect debugger/log output for accidental content leakage before testing real mail.

The server process has no HTTP listener or MCP OAuth flow in this design. The parent client decides which model receives tool results. Document that client-side privacy decision when connecting real mail.

## Handoff and maintenance

At the end of each implementation task:

- Update only completed acceptance checkboxes, and record actual verification evidence in `PLAN.md`.
- Review [CONFIGURATION.md](CONFIGURATION.md) and [USAGE.md](USAGE.md), updating affected settings, workflows, capabilities, limits, and troubleshooting in the same task; keep README links current.
- Update design/interface documentation if implementation exposes a necessary clarification; raise safety/scope changes before implementing them.
- State pending work and limitations clearly, especially unrun live tests.
- Review changed files for credentials, real mail, unbounded reads, stdout contamination, and read/write IMAP paths.

For dependency upgrades, inspect release notes/API changes and intentionally refresh pins/lock files. Re-run the protocol safety suite for MailKit changes. Record a tested Bridge version only after live acceptance; do not equate latest documentation with tested compatibility.
