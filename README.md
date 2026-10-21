# Proton Mail MCP Server

A local, read-only MCP server under development for accessing a Proton Mail mailbox through Proton Mail Bridge on Linux.

**Status:** Phases 1–3 implemented: .NET 10 foundation and a read-only MailKit adapter verified with synthetic TLS/IMAP fixtures. The three approved tools are registered and tested through the official SDK client. An operator-performed real Bridge smoke test succeeded on 2026-10-18 for initialization, tool discovery, folder listing, and date-range search. Full live acceptance remains pending. See [PLAN.md](PLAN.md) for evidence and remaining phases. That test used the former internal-date filter; sender-date filtering now replaces it and awaits live verification. See [usage](docs/USAGE.md) for the updated inputs.

## Configuration and usage

Start with the [configuration guide](docs/CONFIGURATION.md) for external settings, private credential files, folder access policies, and certificate pinning. Copy the [example configuration](examples/config.example.json) outside the checkout before editing real settings. Follow the [usage guide](docs/USAGE.md) to build, verify, launch over stdio, and interpret diagnostics. The host exposes the three approved read-only tools.

## MVP

- C# and .NET 10, the official MCP C# SDK, and MailKit/MimeKit.
- One trusted local client, one Bridge account, and MCP over stdio.
- A narrow folder allowlist, initially `INBOX`, or explicit `AllExcept` access for all folders and labels with optional exclusions.
- Three tools: `list_folders`, `search_messages`, and `get_message`.
- Header/date/unread searches with cursor pagination, text bodies, and attachment metadata.
- No sending, deleting, moving, copying, modifying, expunging, or marking messages as read.

There is no HTTP service, body search, attachment download, background synchronization, or persistent mail cache in the MVP.

## Documentation

| Document | Purpose |
| --- | --- |
| [Linux/Bridge setup](docs/LINUX-SETUP.md) | Installation, public-certificate export/replacement, and operator preparation |
| [Configuration guide](docs/CONFIGURATION.md) | Settings, file permissions, credentials, folder policies, and TLS pins |
| [Usage guide](docs/USAGE.md) | Build/verification, stdio launch, current capabilities, and troubleshooting |
| [AGENTS.md](AGENTS.md) | Instructions for Codex and other coding agents |
| [DESIGN.md](DESIGN.md) | Requirements, architecture, interfaces, and trade-offs |
| [Search pagination](docs/SEARCH-PAGINATION.md) | Implemented search cursors, mailbox-change behavior, and acceptance criteria; live verification pending |
| [PLAN.md](PLAN.md) | Implementation phases, progress, and acceptance criteria |
| [BACKLOG.md](BACKLOG.md) | Requested future features and proposals awaiting implementation scope |
| [SECURITY.md](SECURITY.md) | Trust boundaries and mandatory safety controls |
| [Testing guide](docs/TESTING.md) | Automated checks and opt-in Bridge acceptance tests |
| [Development guide](docs/DEVELOPMENT.md) | Intended setup, development workflow, and handoff prompts |

Start with `AGENTS.md`, then `PLAN.md`. For an implementation task, read the linked design, security, and testing requirements before editing code.

## Prerequisites

- Linux and the .NET 10 SDK. SDK 10.0.112 is pinned in `global.json` with roll-forward disabled.
- VS Code and suitable C# tooling; Codex is the intended coding assistant.
- For live acceptance only: an eligible Proton subscription, a working Proton Mail Bridge installation and credential store, Bridge IMAP credentials, and its exported public TLS certificate. See [Proton's Linux requirements](https://proton.me/support/bridge-for-linux).

Automated development and tests must work without a Proton account or personal mailbox.

## Privacy boundary

The server receives decrypted mail from Bridge. The configured policy controls exposed folders, and a local MCP client can forward returned text to its model provider. `AllExcept` automatically permits new folders and labels; excluding a label folder does not hide its messages through other permitted folders. Local transport does not imply local model processing. See [SECURITY.md](SECURITY.md) before enabling access to real mail.

## Next step

Follow [Linux/Bridge setup](docs/LINUX-SETUP.md) and the [opt-in acceptance procedure](docs/TESTING.md#opt-in-real-bridge-acceptance). Phase 4 tooling and documentation are implemented; live acceptance and MVP completion remain unverified.
