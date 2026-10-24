# Proton Mail MCP Server

A local, read-only MCP server under development for accessing a Proton Mail mailbox through Proton Mail Bridge on Linux.

**Status:** The three base tools and optional attachment downloads are implemented and synthetically tested. Full Bridge acceptance and intended-client PDF interpretation remain pending. See [PLAN.md](PLAN.md) for evidence and outstanding checks.

## Configuration and usage

Start with the [configuration guide](docs/CONFIGURATION.md) for external settings, private credential files, folder access policies, and certificate pinning. Copy the [example configuration](examples/config.example.json) outside the checkout before editing real settings. Follow the [usage guide](docs/USAGE.md) to build, verify, launch over stdio, and interpret diagnostics. The host exposes three base tools and an optional configured attachment-download tool.

## MVP

- C# and .NET 10, the official MCP C# SDK, and MailKit/MimeKit.
- One trusted local client, one Bridge account, and MCP over stdio.
- A narrow folder allowlist, initially `INBOX`, or explicit `AllExcept` access for all folders and labels with optional exclusions.
- Three base tools: `list_folders`, `search_messages`, and `get_message`; optional `download_attachment` when private storage is configured.
- Header/date/unread searches with cursor pagination, text bodies, and attachment metadata.
- No sending, deleting, moving, copying, modifying, expunging, or marking messages as read.

There is no HTTP service, body search, background synchronization, or persistent mail cache.

The opt-in [attachment-download tool](docs/ATTACHMENT-DOWNLOADS.md) saves original files into a configured private directory and returns paths/metadata for the local AI client's document tools. Files persist until operator cleanup; the client must be able to access that filesystem.

## Documentation

| Document | Purpose |
| --- | --- |
| [Linux/Bridge setup](docs/LINUX-SETUP.md) | Installation, public-certificate export/replacement, and operator preparation |
| [Configuration guide](docs/CONFIGURATION.md) | Settings, file permissions, credentials, folder policies, and TLS pins |
| [Usage guide](docs/USAGE.md) | Build/verification, stdio launch, current capabilities, and troubleshooting |
| [AGENTS.md](AGENTS.md) | Instructions for Codex and other coding agents |
| [DESIGN.md](DESIGN.md) | Requirements, architecture, interfaces, and trade-offs |
| [Search pagination](docs/SEARCH-PAGINATION.md) | Implemented search cursors, mailbox-change behavior, and acceptance criteria; live verification pending |
| [Attachment downloads](docs/ATTACHMENT-DOWNLOADS.md) | Implemented private-directory downloads, storage/retention rules and pending client/live acceptance |
| [PLAN.md](PLAN.md) | Implementation phases, progress, and acceptance criteria |
| [BACKLOG.md](BACKLOG.md) | Deferred proposals requiring scope agreement |
| [SECURITY.md](SECURITY.md) | Trust boundaries and mandatory safety controls |
| [Testing guide](docs/TESTING.md) | Automated checks and opt-in Bridge acceptance tests |
| [Development guide](docs/DEVELOPMENT.md) | Build/test commands, dependency maintenance and debugging |
| [Implementation history](docs/archive/IMPLEMENTATION-HISTORY.md) | Archived authorizations, completed checklists and verification evidence |

Start with `AGENTS.md`, then `PLAN.md`. For an implementation task, read the linked design, security, and testing requirements before editing code.

## Prerequisites

- Linux and the .NET 10 SDK. SDK 10.0.113 is pinned in `global.json` with roll-forward disabled.
- VS Code and suitable C# tooling; Codex is the intended coding assistant.
- For live acceptance only: an eligible Proton subscription, a working Proton Mail Bridge installation and credential store, Bridge IMAP credentials, and its exported public TLS certificate. See [Proton's Linux requirements](https://proton.me/support/bridge-for-linux).

Automated development and tests must work without a Proton account or personal mailbox.

## Privacy boundary

The server receives decrypted mail from Bridge. The configured policy controls exposed folders, and a local MCP client can forward returned text to its model provider. `AllExcept` automatically permits new folders and labels; excluding a label folder does not hide its messages through other permitted folders. Local transport does not imply local model processing. See [SECURITY.md](SECURITY.md) before enabling access to real mail.

## Next step

Follow [Linux/Bridge setup](docs/LINUX-SETUP.md) and the [opt-in acceptance procedure](docs/TESTING.md#opt-in-real-bridge-acceptance). Current acceptance gaps are tracked in [PLAN.md](PLAN.md).
