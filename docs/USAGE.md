# Usage guide

The server exposes exactly three read-only MCP tools over stdio: `list_folders`, `search_messages`, and `get_message`. Phases 1–3 have synthetic automated coverage. A manual real Bridge smoke test succeeded on 2026-10-18 for initialization, discovery, folder listing, and date-range search; full live acceptance and version-specific compatibility remain pending. See [PLAN.md](../PLAN.md).

## Build and verify

Use Linux and the .NET SDK pinned in `global.json` (currently 10.0.112, with roll-forward disabled). From the repository root:

```bash
dotnet restore ProtonMailMcp.slnx --locked-mode
dotnet build ProtonMailMcp.slnx --configuration Release --no-restore
dotnet test ProtonMailMcp.slnx --configuration Release --no-build --filter 'Category!=LiveBridge'
```

The standard suite uses synthetic configuration, TLS certificates, and ephemeral loopback IMAP fixtures. It needs no Proton account, real credentials, or running Bridge. Restore may require NuGet network access. Some restricted runners must permit MSBuild/VSTest IPC and synthetic loopback sockets; see the [testing guide](TESTING.md).

## Configure the host

Follow the [Linux/Bridge setup guide](LINUX-SETUP.md) and [configuration guide](CONFIGURATION.md) to prepare an external JSON file, separate private password file, public certificate, and explicit folder allowlist. Examples use placeholders; keep actual settings outside the repository. Linux `statx` support is required for password-file checks.

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

The parent client owns stdin/stdout, initializes the MCP session, and closes the transport on shutdown. The process is not an interactive shell for mail commands and does not provide an HTTP endpoint. Stdout is reserved for MCP messages; capture stderr separately and do not merge it into stdout. A missing startup banner is expected. Closing stdin allows clean shutdown; an active operation may drain until its existing 30-second deadline before exit. For prompt cancellation, send an MCP cancellation notification before closing the transport.

Successful initialization identifies the server as `ProtonMailMcp`. It advertises the three tools below, with read-only and non-destructive annotations, and no resources or prompts. Initialization and discovery do not connect to Bridge.

## Mailbox functionality and limits

The tools call the tested adapter for allowlisted folder discovery, typed searches, and UID-based text retrieval. The following contracts use case-sensitive camelCase JSON names. Unknown arguments and incorrect JSON types return `invalid_input`. Optional substring/date/unread filters accept omission or null; `limit` must be an integer when supplied.

| Behavior | Implemented tool boundary |
| --- | --- |
| Search | Sender, To/Cc recipient, subject, delivery dates, unread status; default 20 results, maximum 50, highest UIDs first |
| Message identity | Folder full name + UIDVALIDITY + UID; stale identities are rejected |
| Body | Primary non-attachment plain text, otherwise HTML converted to text without rendering or fetching external references |
| Transfer/output | Selected text part at most 1 MiB; returned text at most 32,000 characters, with explicit status/truncation |
| Attachments | Metadata only, at most 100 entries with an attachment truncation flag; no payload downloads |
| Connection | One encrypted connection per operation, serialized access, 30-second deadline including queue time |
| Safety | Read-only folder selection and PEEK; no sending, flag changes, mailbox mutation, or expunging cleanup |

Message content, links, and filenames remain untrusted data. A local client may forward tool results to a hosted model provider; stdio transport does not guarantee local model processing. Read [SECURITY.md](../SECURITY.md) before enabling real-mail access.

## Tool inputs and results

| Tool | Arguments | Successful structured result |
| --- | --- | --- |
| `list_folders` | `{}` or omitted arguments | `{"folders":[{"name":"INBOX"}]}`; only existing selectable allowed folders |
| `search_messages` | Required `folder`; optional `sender`, `recipient`, `subject`, `receivedStart`, `receivedEnd`, `unread`, `limit` | `{"messages":[summary],"truncated":false}` |
| `get_message` | Required `folder`, `uidValidity`, `uid` | `{"summary":summary,"text":"...","contentStatus":"available","truncated":false,"attachments":[],"attachmentsTruncated":false}` |

A summary has `reference` (`folder`, `uidValidity`, `uid`), `from`, `to`, `cc`, `subject`, `senderDate`, `internalDate`, `seen`, and `size`. Optional metadata is explicitly null when absent. Dates in results use ISO 8601 timestamps; size is reported bytes. Preserve all three reference fields from a search result when calling `get_message`; identifiers are integers from 1 through 4,294,967,295.

Search substrings must be nonblank, control-free, and at most 1,024 characters. Filters combine with AND; recipient matches To or Cc. Delivery dates are exact `YYYY-MM-DD`, inclusive start and exclusive end, with start strictly before end if both are supplied. Unread omitted/null means either status, true means unseen, false means seen. Limit defaults to 20 and must be 1–50. Results use descending UID order, which need not match sender chronology. Narrow filters if `truncated` is true.

`contentStatus` is one of `available`, `missing`, `unsupported`, `oversized`, or `malformed`. Unavailable text is null; an available empty body may be an empty string. Metadata-only content statuses are successful results. `truncated` describes the body character cap; `attachmentsTruncated` describes the 100-entry metadata cap. Each attachment has nullable `filename`, `mimeType`, and reported `size`. Filenames are data and must never be treated as paths. No attachment payload is returned.

Successes populate MCP `structuredContent` and a text content block containing the same JSON for compatible clients. Tool execution failures set `isError: true`, omit structured content, and return one text block containing only the fixed code below. They never masquerade as empty successful results. Received MCP cancellation notifications cancel the adapter operation and release its connection. With the pinned SDK in this environment, cancelling a high-level client call’s local token did not reliably deliver remote cancellation; a cancelled local await alone is not evidence of server cleanup. The process regression explicitly sends and awaits `notifications/cancelled` for a known request ID using `McpClient.SendMessageAsync`. See the [test evidence](TESTING.md#phase-3-coverage-and-limitations) and [explicit cancellation example in the process tests](../tests/ProtonMailMcp.Tests/Mcp/ToolTests.cs).

| Error code | Meaning |
| --- | --- |
| `invalid_input` | Missing, unknown, malformed, or out-of-range arguments |
| `denied_folder` | Folder is outside the exact allowlist |
| `folder_unavailable` | Allowed folder is missing or not selectable |
| `bridge_unavailable` | Connection refused/lost or malformed protocol response |
| `authentication_failed` | Bridge rejected authentication |
| `tls_failed` | Required TLS or certificate verification failed |
| `stale_reference` | UIDVALIDITY changed; search again rather than reuse the UID |
| `message_not_found` | UID is missing or disappeared during retrieval |
| `timeout` | The 30-second operation deadline expired, including queue time |
| `protocol_failure` | Rejected command, unconfirmed read-only access, or unexpected internal failure |

Each completed/cancelled tool call emits a stderr diagnostic containing only operation name, random correlation ID, elapsed milliseconds, and fixed outcome. SDK/framework payload logging remains disabled. No folder, identifier, filters, mail, credentials, or exception text is logged.

## Official SDK client example

The process suite verifies this API shape using pinned `ModelContextProtocol` 2.2.0 and synthetic TLS/IMAP fixtures. Run the suite with `dotnet test ProtonMailMcp.slnx --configuration Release --no-build --filter 'Category=Mcp'`. The standalone example below requires operator-prepared external configuration; it is not a live acceptance claim.

```csharp
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

await using var client = await McpClient.CreateAsync(
    new StdioClientTransport(new StdioClientTransportOptions
    {
        Command = "dotnet",
        Arguments = ["/absolute/path/to/ProtonMailMcp.dll", "--config",
                     "/absolute/path/to/config.json"],
        Name = "ProtonMailMcp"
    }));
var tools = await client.ListToolsAsync();
var folders = await client.CallToolAsync("list_folders");
var search = await client.CallToolAsync("search_messages",
    new Dictionary<string, object?> { ["folder"] = "INBOX", ["unread"] = true });
// Check IsError before consuming StructuredContent. Take the reference from a result.
if (search.IsError != true && search.StructuredContent is { } data &&
    data.GetProperty("messages").GetArrayLength() > 0)
{
    var reference = data.GetProperty("messages")[0].GetProperty("reference");
    var message = await client.CallToolAsync("get_message",
        new Dictionary<string, object?>
        {
            ["folder"] = reference.GetProperty("folder").GetString(),
            ["uidValidity"] = reference.GetProperty("uidValidity").GetUInt32(),
            ["uid"] = reference.GetProperty("uid").GetUInt32()
        });
}
```

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

An operator-performed manual real Bridge smoke test succeeded on 2026-10-18 for MCP initialization, discovery of the three approved tools, folder listing, and bounded delivery-date-range search with truncation reporting. Bridge/Linux/runtime versions were not supplied for that run. Message-body retrieval, exact live date-boundary exclusion, before/after persistent flag and membership preservation, and failure/recovery behavior remain unverified. No mailbox output is retained in the documentation.

The live acceptance suite requires an exact opt-in phrase, external settings, a dedicated folder and a known-fixture manifest; it has not been run against Bridge. Standard tests never discover credentials or use an existing Bridge listener. Follow [TESTING.md](TESTING.md#opt-in-real-bridge-acceptance) for the implemented operator-prepared acceptance procedure; keep Phase 4 incomplete until actual evidence exists.

## VS Code MCP launch

In VS Code, use **MCP: Add Server** and choose a stdio command. Prefer user-profile storage for real-mail configuration paths. The equivalent configuration below uses placeholders; follow [VS Code's MCP server guide](https://code.visualstudio.com/docs/agent-customization/mcp-servers) for the installed client version. This client example is documented, not interactively verified in this handoff.

```json
{
  "servers": {
    "protonmail": {
      "type": "stdio",
      "command": "dotnet",
      "args": [
        "/absolute/path/to/checkout/src/ProtonMailMcp/bin/Release/net10.0/ProtonMailMcp.dll",
        "--config",
        "/absolute/private/directory/config.json"
      ]
    }
  }
}
```

Review the client's trust prompt and model/data policy before enabling tools. Use **MCP: List Servers** to start/stop/restart the process and inspect sanitized stderr. Discovery should show exactly the three tools. Restart after changing configuration, credentials, or pins. A client tool call can access real mail; do not test it with personal settings before authorization. Never use `dotnet run` as the MCP transport command or merge stderr into stdout.

## Runtime troubleshooting

| Symptom | Safe operator action |
| --- | --- |
| `bridge_unavailable` | Start/unlock Bridge locally, wait for sync, check the displayed IMAP loopback endpoint and port, then retry deliberately. Initialization can succeed while Bridge is down. No automatic retries occur. |
| `authentication_failed` | Check Bridge's generated IMAP username/password locally. If changed, update only the external password/configuration and restart the host. Do not use the Proton account password or inspect keyrings/caches. |
| `tls_failed` | Check required mode, local clock, public certificate validity and intended Bridge instance. Follow [certificate replacement](LINUX-SETUP.md#export-and-replace-the-certificate); do not accept a network-learned pin or disable verification. |
| `denied_folder` or absent discovery entry | Check exact full-name allowlisting; missing/nonselectable entries are omitted. A parent does not authorize children. The server never creates or subscribes folders. |
| `stale_reference` | Search again and use the new folder + UIDVALIDITY + UID. During acceptance stop and investigate a changed epoch before preparing a new external manifest. |
| `message_not_found` | A message disappeared or the identity is wrong. Search again; never substitute sequence number or Message-ID. |
| `timeout` / connection lost | Check Bridge availability and competing calls. Each operation has 30 seconds including queue time. A later call uses a fresh connection. Send an MCP cancellation notification to cancel remotely. |
| Truncated search/body | Narrow search filters; maximum 50 results and 32,000 text characters. There is no pagination or limit bypass. |
| `oversized`, `unsupported`, `missing`, `malformed` | Inspect status and metadata. Selected bodies over 1 MiB are not fetched; malformed or unavailable bodies stay metadata-only. Attachment content and alternate-body fallback to evade limits are unsupported. |
| `protocol_failure` / read-only rejection | Stop live testing and reproduce with synthetic fixtures. Do not enable SELECT/read-write, remove PEEK, or weaken TLS to work around Bridge behavior. |
| Live state comparison failed | Stop other clients/rules, inspect fixtures locally using read-only access, and investigate changes. Never reset the baseline silently or clean up flagged messages with this suite. |

Report only fixed codes, versions, and pass/fail outcomes. Do not enable production wire logging, paste message results, or attach raw settings to diagnose failures.
