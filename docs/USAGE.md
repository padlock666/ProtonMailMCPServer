# Usage guide

The current server can load validated configuration and initialize an MCP session over stdio. Phases 1 and 2 are implemented: the internal read-only mailbox adapter has synthetic protocol coverage, but **no MCP tools, resources, or prompts are exposed yet**. This release cannot search or retrieve mail through an MCP client. See [PLAN.md](../PLAN.md) for implementation and acceptance status.

## Build and verify

Use Linux and the .NET SDK pinned in `global.json` (currently 10.0.112, with roll-forward disabled). From the repository root:

```bash
dotnet restore ProtonMailMcp.slnx --locked-mode
dotnet build ProtonMailMcp.slnx --configuration Release --no-restore
dotnet test ProtonMailMcp.slnx --configuration Release --no-build --filter 'Category!=LiveBridge'
```

The standard suite uses synthetic configuration, TLS certificates, and ephemeral loopback IMAP fixtures. It needs no Proton account, real credentials, or running Bridge. Restore may require NuGet network access. Some restricted runners must permit MSBuild/VSTest IPC and synthetic loopback sockets; see the [testing guide](TESTING.md).

## Configure the host

Follow the [configuration guide](CONFIGURATION.md) to prepare an external JSON file, separate private password file, public certificate, and explicit folder allowlist. Examples use placeholders; keep actual settings outside the repository. Linux `statx` support is required for password-file checks.

A successful startup validates local configuration only. The adapter is registered lazily, and initialization makes no mailbox connection or authentication attempt. Startup success therefore does not prove that Bridge is available or that its live peer certificate and credentials match.

## Launch over stdio

Launch the built artifact, so build output cannot enter the MCP protocol stream. From the repository root, the command is:

```bash
dotnet src/ProtonMailMcp/bin/Release/net10.0/ProtonMailMcp.dll --config /absolute/path/to/config.json
```

For a parent MCP client's stdio launch settings, use the following command and argument order. Substitute absolute paths on the operator's machine; this is a launch specification, not a client-specific configuration schema.

| Field | Value |
| --- | --- |
| Command | `dotnet` |
| Argument 1 | `/absolute/path/to/checkout/src/ProtonMailMcp/bin/Release/net10.0/ProtonMailMcp.dll` |
| Argument 2 | `--config` |
| Argument 3 | `/absolute/path/to/config.json` |

The parent client owns stdin/stdout, initializes the MCP session, and closes the transport on shutdown. The process is not an interactive shell for mail commands and does not provide an HTTP endpoint. Stdout is reserved for MCP messages; capture stderr separately and do not merge it into stdout. A missing startup banner is expected. Closing stdin is covered by the synthetic process tests and allows clean shutdown.

Successful initialization identifies the server as `ProtonMailMcp`. It currently advertises no tools, resources, or prompts. Client-specific installation instructions and mailbox tool-call examples will be added when those interfaces are implemented and tested.

## Mailbox functionality and limits

The internal adapter implements allowlisted folder discovery, typed header/date/unread searches, and UID-based text retrieval. The planned MCP tools are `list_folders`, `search_messages`, and `get_message`; their public contracts remain Phase 3 work. See [DESIGN.md](../DESIGN.md) for approved behavior rather than treating the following as a current callable interface.

| Behavior | Implemented adapter boundary |
| --- | --- |
| Search | Sender, To/Cc recipient, subject, delivery dates, unread status; default 20 results, maximum 50, highest UIDs first |
| Message identity | Folder full name + UIDVALIDITY + UID; stale identities are rejected |
| Body | Primary non-attachment plain text, otherwise HTML converted to text without rendering or fetching external references |
| Transfer/output | Selected text part at most 1 MiB; returned text at most 32,000 characters, with explicit status/truncation |
| Attachments | Metadata only, at most 100 entries with an attachment truncation flag; no payload downloads |
| Connection | One encrypted connection per operation, serialized access, 30-second deadline including queue time |
| Safety | Read-only folder selection and PEEK; no sending, flag changes, mailbox mutation, or expunging cleanup |

Message content, links, and filenames remain untrusted data. A local client may forward future tool results to a hosted model provider; stdio transport does not guarantee local model processing. Read [SECURITY.md](../SECURITY.md) before enabling real-mail access.

## Startup diagnostics

| Exit / diagnostic | Meaning and next check |
| --- | --- |
| `0` | Normal shutdown; not evidence of live mailbox connectivity |
| `2`, `startup: configuration_usage_requires_config_path` | Supply exactly `--config` and one absolute configuration path |
| `2`, `configuration_absolute_path_required` | Check the configuration, password, and certificate paths |
| `2`, `configuration_host_invalid`, `configuration_port_invalid`, or `configuration_tls_mode_invalid` | Check the literal loopback address, port range, and exact TLS mode value |
| `2`, `configuration_username_invalid` or `configuration_allowlist_invalid` | Check the required username and nonempty exact folder list |
| `2`, `configuration_password_file_unavailable`, `configuration_password_file_unsafe`, or `configuration_password_directory_unsafe` | Check existence, ownership, private modes, regular-file type, size, and final-component symlinks |
| `2`, `configuration_password_invalid` | Check UTF-8 password content and line endings without printing the secret |
| `2`, `configuration_public_certificate_required` or `configuration_certificate_invalid` | Check that the file contains a single public certificate and that its validity dates include the current time |
| `2`, `configuration_file_too_large` | Check the file limits in the configuration guide |
| `2`, `configuration_linux_required` or `configuration_password_metadata_unavailable` | Use Linux with working opened-handle ownership/permission checks |
| `2`, `configuration_invalid` or `configuration_invalid_or_unavailable` | Check JSON spelling/types, readability, certificate format, and password encoding locally |
| `1`, `host: failed` | The host failed; review the build/runtime setup and reproduce with synthetic fixtures |

All configuration diagnostics carry the `startup: ` prefix, including rows that abbreviate it. Failures intentionally omit raw exceptions and sensitive values. Share only sanitized codes when reporting problems. Restart after fixing external settings; the server does not reload them automatically.

## Live acceptance status

No Bridge version or live mailbox behavior has been validated. The live acceptance suite and its explicit opt-in switch are not implemented, so no live test command is provided. Standard tests never discover credentials or use an existing Bridge listener. Follow [TESTING.md](TESTING.md#opt-in-real-bridge-acceptance) for the planned operator-prepared acceptance procedure; keep Phase 4 incomplete until actual evidence exists.
