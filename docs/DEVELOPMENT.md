# Development and handoff guide

Build, dependency maintenance and debugging workflow. See [PLAN.md](../PLAN.md) for implementation status and pending live acceptance.

## Working with Codex

Open the repository root in VS Code and begin with [AGENTS.md](../AGENTS.md), the instruction entry point linking the plan, design, security and testing requirements. Keep detailed behavior in those documents; create extra agent configuration only for a concrete need. Start a fresh session if updated instructions appear stale.

Specify the authorized scope, intended result, constraints and required evidence in implementation requests. Live access always requires separate explicit opt-in and operator-prepared fixtures. Keep secrets and personal mail out of prompts.

Optional assistant setting: use the model available in your client and increase reasoning effort for difficult IMAP/security issues. Settings do not replace protocol tests or review; the server itself calls no model API.

## Toolchain and layout

Use the pinned .NET SDK, Generic Host, official MCP SDK, MailKit/MimeKit and xUnit. Keep one `ProtonMailMcp.slnx`, one application in `src/ProtonMailMcp` and one test project in `tests/ProtonMailMcp.Tests`; see [DESIGN.md](../DESIGN.md) for architecture. Do not add projects, a web host, database, container or service installer for the MVP.

Keep nullable types enabled and use cancellable asynchronous I/O. Ignore generated output and local secrets before creating files; ignore rules do not authorize storing real data in the checkout.

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

`global.json` pins SDK **10.0.113**, disables roll-forward, and excludes prereleases. Both projects target `net10.0` with nullable types and warnings as errors. Select a different SDK only by an intentional pin update.

| Dependency | Exact version | Rationale |
| --- | --- | --- |
| ModelContextProtocol / Core | 2.2.0 | Official stdio/Generic Host SDK; net10.0 assets and APIs verified from restored XML docs, package metadata, and process tests |
| Microsoft.Extensions.Hosting | 10.0.13 | Generic Host without ASP.NET hosting; explicit defaults disabled |
| MailKit / transitive MimeKit | 4.18.1 / 4.18.1 | Pinned mail stack verified by the Phase 2 synthetic protocol suite |
| System.Security.Cryptography.Pkcs | 10.0.13 | Direct override of MimeKit's vulnerable 10.0.0 minimum |
| Microsoft.NET.Test.Sdk | 18.11.0 | VSTest runner for net10.0 |
| xUnit / Visual Studio adapter | 2.9.3 / 4.0.1 | xUnit v2 tests and compatible adapter |

Each project has a `packages.lock.json` fixing the complete graph and hashes. NuGet.org is the explicit package source. Publication dates, target frameworks, dependencies, and package release metadata were reviewed before pinning; successful restore/build/process tests establish Phase 1 API compatibility. The [SDK getting-started guide](https://github.com/modelcontextprotocol/csharp-sdk/blob/6fa3825973949a9c4f0cd8af344e15a8db09dc35/docs/concepts/getting-started.md) is a reference; this implementation uses no assembly tool discovery. The PKCS override addresses [Microsoft's October advisory](https://github.com/advisories/GHSA-846f-f764-76mx) and [September advisory](https://github.com/advisories/GHSA-2fmv-rv2c-59x7). NuGet auditing remains enabled for the entire graph, with warnings treated as errors.

For an intentional dependency update, edit exact package references, run `dotnet restore ProtonMailMcp.slnx --force-evaluate`, review both lock files and security advisories, then run the locked restore/build/test commands. MailKit upgrades require protocol regression; Bridge compatibility claims require live evidence. Check SDK/runtime security updates independently; package audit does not audit the installed runtime.

## Documentation maintenance

Give each fact a primary home and link to it:

| Document | Responsibility |
| --- | --- |
| [PLAN.md](../PLAN.md) / [history](archive/IMPLEMENTATION-HISTORY.md) | Current scope, acceptance gaps and latest checks / historical evidence and completed plans |
| [DESIGN.md](../DESIGN.md) | Architecture and implementation responsibilities |
| [SECURITY.md](../SECURITY.md) | Mandatory controls and trust boundaries |
| [CONFIGURATION.md](CONFIGURATION.md) | Settings, permissions and configuration validation |
| [USAGE.md](USAGE.md) | Tool contracts, launch, limits, diagnostics and client workflows |
| [Feature contracts](SEARCH-PAGINATION.md) and [downloads](ATTACHMENT-DOWNLOADS.md) | Detailed algorithms, edge cases and acceptance matrices |
| [TESTING.md](TESTING.md) | Coverage, test limitations and exact live procedure |

Update affected guides and [config.example.json](../examples/config.example.json) in the same task. Review CONFIGURATION and USAGE for each implementation change; keep README navigation current. Use placeholders, document only implemented behavior as available, and retain short safety reminders at sensitive actions.

Validate local links/anchors, fenced JSON, commands and documented limits against the implementation. Record checks actually run and remaining limitations in PLAN. Documentation work does not authorize credential discovery or live access.

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

## Completion

Update acceptance criteria only with evidence. Record commands, results, environmental limits and unrun live work in PLAN. Review changes for secrets/content leakage, stdout contamination and unsafe mailbox paths. Raise scope or safety changes before implementing them.
