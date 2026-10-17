# Proton Mail MCP Server

A local, read-only MCP server under development for accessing a Proton Mail mailbox through Proton Mail Bridge on Linux.

**Status:** Phase 1 foundation implemented: .NET 10 solution, validated external configuration, stdio initialization, and synthetic offline tests. Mailbox operations and the three tools remain unimplemented. No Bridge or live-mail validation has been performed. See [PLAN.md](PLAN.md) for evidence and remaining phases.

## MVP

- C# and .NET 10, the official MCP C# SDK, and MailKit/MimeKit.
- One trusted local client, one Bridge account, and MCP over stdio.
- A folder allowlist, initially `INBOX`.
- Three tools: `list_folders`, `search_messages`, and `get_message`.
- Header/date/unread searches, text bodies, and attachment metadata.
- No sending, deleting, moving, copying, modifying, expunging, or marking messages as read.

There is no HTTP service, body search, attachment download, background synchronization, or persistent mail cache in the MVP.

## Documentation

| Document | Purpose |
| --- | --- |
| [AGENTS.md](AGENTS.md) | Instructions for Codex and other coding agents |
| [DESIGN.md](DESIGN.md) | Requirements, architecture, interfaces, and trade-offs |
| [PLAN.md](PLAN.md) | Implementation phases, progress, and acceptance criteria |
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

The server receives decrypted mail from Bridge. Only configured folders are exposed, but a local MCP client can forward returned text to its model provider. Local transport does not imply local model processing. See [SECURITY.md](SECURITY.md) before enabling access to real mail.

## Next step

Review Phase 1 evidence, then request Phase 2 using the [development guide](docs/DEVELOPMENT.md). Phase 2 implements the read-only adapter against synthetic protocol fixtures; live acceptance remains Phase 4.
