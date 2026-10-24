# Usage guide

Optional [attachment downloads](ATTACHMENT-DOWNLOADS.md) save original bytes into a configured private directory and return paths for the local client's document tools. Enable them with [AttachmentDownloads.Directory](CONFIGURATION.md#optional-attachment-downloads).

By default the server exposes three read-only MCP tools over stdio: `list_folders`, `search_messages`, and `get_message`. Phases 1–3 have synthetic automated coverage. A manual real Bridge smoke test succeeded on 2026-10-18 for initialization, discovery, folder listing, and date-range search; full live acceptance and version-specific compatibility remain pending. See [PLAN.md](../PLAN.md).

## Build and verify

Use Linux and the .NET SDK pinned in `global.json` (currently 10.0.113, with roll-forward disabled). From the repository root:

```bash
dotnet restore ProtonMailMcp.slnx --locked-mode
dotnet build ProtonMailMcp.slnx --configuration Release --no-restore
dotnet test ProtonMailMcp.slnx --configuration Release --no-build --filter 'Category!=LiveBridge'
```

The standard suite uses synthetic configuration, TLS certificates, and ephemeral loopback IMAP fixtures. It needs no Proton account, real credentials, or running Bridge. Restore may require NuGet network access. Some restricted runners must permit MSBuild/VSTest IPC and synthetic loopback sockets; see the [testing guide](TESTING.md).

If the CLI reports “A compatible .NET SDK was not found”, compare `dotnet --list-sdks` with `global.json`. Roll-forward is disabled, so even a newer installed patch does not satisfy a different exact pin. Install the pinned SDK or intentionally update the pin and verify the build/tests; see the [toolchain policy](DEVELOPMENT.md#pinned-dependencies-and-verification-sources).

## Configure the host

Follow the [Linux/Bridge setup guide](LINUX-SETUP.md) and [configuration guide](CONFIGURATION.md) to prepare an external JSON file, separate private password file, public certificate, and explicit folder access policy. Examples use placeholders; keep actual settings outside the repository. Linux `statx` support is required for opened-handle ownership and permission checks on all three files.

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

Successful initialization identifies the server as `ProtonMailMcp`. It advertises three base tools with read-only/non-destructive annotations and the fourth `download_attachment` tool when configured; the download tool is a non-destructive local write (`readOnlyHint: false`, `idempotentHint: false`), and no resources or prompts. Initialization and discovery do not connect to Bridge.

## Mailbox functionality and limits

The tools call the tested adapter for permitted folder discovery, typed searches, and UID-based text retrieval. The following contracts use case-sensitive camelCase JSON names. Unknown arguments and incorrect JSON types return `invalid_input`. Optional substring/date/unread filters accept omission or null; `limit` must be an integer when supplied.

| Behavior | Implemented tool boundary |
| --- | --- |
| Search | Sender, To/Cc recipient, subject, sender dates, unread status; cursor pagination, default 20 results per page, maximum 50, highest UIDs first |
| Message identity | Folder full name + UIDVALIDITY + UID; stale identities are rejected |
| Body | Primary non-attachment plain text, otherwise HTML converted to text without rendering or fetching external references |
| Transfer/output | Selected text part at most 1 MiB; returned text at most 32,000 characters, with explicit status/truncation |
| Attachments | `get_message` returns at most 100 metadata entries/IDs and a truncation flag; configured downloads save one eligible attachment at a time |
| Connection | One encrypted connection per operation, serialized access, 30-second deadline including queue time |
| Safety | Read-only folder selection and PEEK; no sending, flag changes, mailbox mutation, or expunging cleanup |

Message content, links, and filenames remain untrusted data. A local client may forward tool results to a hosted model provider; stdio transport does not guarantee local model processing. Read [SECURITY.md](../SECURITY.md) before enabling real-mail access.

Folder discovery returns at most 4,096 permitted folders and fails without a partial list if this count is exceeded. It follows the configured `Allowlist` or `AllExcept` policy; see [configuration](CONFIGURATION.md#folder-access-policy). AllExcept automatically exposes new folders and labels. Exclusions block folder access only: a message may still be returned through another permitted label/folder, including INBOX or All Mail. Folder discovery has no pagination arguments; search cursors are described below.

## Tool inputs and results

| Tool | Arguments | Successful structured result |
| --- | --- | --- |
| `list_folders` | `{}` or omitted arguments | `{"folders":[{"name":"INBOX"}]}`; only existing selectable permitted folders |
| `search_messages` | Required `folder`; optional `sender`, `recipient`, `subject`, `sentStart`, `sentEnd`, `unread`, `limit`, `cursor` | `{"messages":[summary],"truncated":false,"nextCursor":null}` |
| `download_attachment` (optional) | Required `folder`, `uidValidity`, `uid`, `attachmentId` | Saved absolute `path`, `reference`, `attachmentId`, `originalFilename`, `mimeType`, `reportedSize`, `sizeBytes`, `sha256` |
| `get_message` | Required `folder`, `uidValidity`, `uid` | `{"summary":summary,"text":"...","contentStatus":"available","truncated":false,"attachments":[],"attachmentsTruncated":false}` |

A summary has `reference` (`folder`, `uidValidity`, `uid`), `from`, `to`, `cc`, `subject`, `senderDate`, `internalDate`, `seen`, and `size`. Optional metadata is explicitly null when absent. Dates in results use ISO 8601 timestamps; size is reported bytes. Preserve all three reference fields from a search result when calling `get_message`; identifiers are integers from 1 through 4,294,967,295.

Search substrings must be nonblank, control-free, and at most 1,024 characters. Filters combine with AND; recipient matches To or Cc. Sender dates are exact `YYYY-MM-DD`, inclusive `sentStart` and exclusive `sentEnd`, with start strictly before end if both are supplied. Filtering uses the sender's `Date` header through IMAP `SENTSINCE`/`SENTBEFORE`, ignoring time and timezone (no UTC conversion). There is no internal-date fallback; missing, malformed or inaccurate sender dates can affect matching. This replaces the former delivery-date behavior as of 2026-10-18. The old `receivedStart`/`receivedEnd` arguments return `invalid_input`; update clients and restart the server to refresh tool discovery. Unread omitted/null means either status, true means unseen, false means seen. Limit defaults to 20 and must be 1–50. Results use descending UID order, which need not match sender chronology.

For messages dated during a calendar month, call `search_messages` with:

```json
{"folder":"INBOX","sentStart":"2025-04-01","sentEnd":"2025-05-01","limit":20}
```

To continue, pass the returned `nextCursor` as `cursor` and repeat the same folder and filters. Omission/null are equivalent for filters; INBOX is case-insensitive, while other folder names and substring values must match exactly. `limit` may change; omitting it uses 20 on that call. Omit or pass null for `cursor` to start again. A cursor is an opaque 98-character string; do not construct, edit, or log it.

```json
{"folder":"INBOX","sentStart":"2025-04-01","sentEnd":"2025-05-01","limit":20,"cursor":"<nextCursor copied from previous result>"}
```

`truncated` is true exactly when `nextCursor` is non-null. Stop only on a null cursor: messages can disappear between search and fetch, producing a short or empty page that still has a continuation. Later pages use matching UIDs below the previous candidate boundary, so higher-UID arrivals do not shift them. Each page evaluates filters again; unread changes can add/remove matches, and replaying a cursor can yield different results. This is not a mailbox snapshot. Search may still materialize all matching UIDs within the remaining range.

Cursors are tied to this server process, folder, filters, and UIDVALIDITY. Server restart invalidates them with `invalid_input`; changed folder epochs return `stale_reference`. Start a new search in either case. Refresh tool discovery when upgrading; requests without cursors keep their first-page behavior, but strict clients must accept the added nullable `nextCursor` output. The full [pagination contract](SEARCH-PAGINATION.md) covers cursor lifetime and mailbox-change behavior. Live Bridge pagination remains unverified.

`contentStatus` is one of `available`, `missing`, `unsupported`, `oversized`, or `malformed`. Unavailable text is null; an available empty body may be an empty string. Metadata-only content statuses are successful results. `truncated` describes the body character cap; `attachmentsTruncated` describes the 100-entry metadata cap. Each attachment has nullable `filename`, `mimeType`, reported `size` and nullable `attachmentId`. IDs are available for eligible parts when downloads are configured. Filenames are data and must never be treated as paths. `get_message` never returns or fetches attachment payloads.

To download a PDF, preserve the message reference and use its non-null attachment ID:

```json
{"folder":"INBOX","uidValidity":7,"uid":42,"attachmentId":"2"}
```

Call `download_attachment` with that object. It returns the generated absolute path, sanitized original filename, declared MIME type, server-reported encoded size, exact decoded size and SHA-256. Pass the path to a client tool that can read PDFs on the same filesystem. The server performs MIME transfer decoding only; it does not read PDF text, perform OCR, unlock encrypted PDFs, execute files or fetch links. Attached messages, multipart containers and inline-only body parts are excluded. MIME types/extensions remain untrusted claims. Other eligible binary files are saved as `.bin`; declared PDFs as `.pdf`.

Each success creates a distinct private file retained until operator cleanup, even on repeated requests. There is no overwrite or caller-selected destination. Limits are 36 MiB encoded and 25 MiB decoded, with directory quota 250 MiB/1,000 entries and the existing 30-second deadline. Errors/cancellation before publication attempt to remove only that operation's incomplete file. A process crash can leave private `.part` files; cancellation or lost responses around atomic publication can leave completed files. Do not blindly retry expecting deduplication. The server never automatically deletes completed files.

Download errors include `attachment_not_found`, `attachment_unsupported`, `attachment_too_large`, `attachment_malformed`, `download_directory_unsafe`, `download_limit_exceeded`, `download_storage_failed` and `download_cleanup_failed`. They return no path or content. See [exact meanings and reference validation](ATTACHMENT-DOWNLOADS.md#errors). Client PDF interpretation and live Bridge download acceptance remain pending; synthetic SDK/file handoff verifies transport and bytes only.

Successes populate MCP `structuredContent` and a text content block containing the same JSON for compatible clients. Tool execution failures set `isError: true`, omit structured content, and return one text block containing only the fixed code below. They never masquerade as empty successful results. Received MCP cancellation notifications cancel the adapter operation and release its connection. With the pinned SDK in this environment, cancelling a high-level client call’s local token did not reliably deliver remote cancellation; a cancelled local await alone is not evidence of server cleanup. The process regression explicitly sends and awaits `notifications/cancelled` for a known request ID using `McpClient.SendMessageAsync`. See the [test evidence](TESTING.md#phase-3-coverage-and-limitations) and [explicit cancellation example in the process tests](../tests/ProtonMailMcp.Tests/Mcp/ToolTests.cs).

| Error code | Meaning |
| --- | --- |
| `invalid_input` | Missing, unknown, malformed, or out-of-range arguments; invalid/tampered cursor, changed cursor filters, or cursor from another server process |
| `denied_folder` | Folder is outside the allowlist or is explicitly excluded in AllExcept mode |
| `folder_unavailable` | Allowed folder is missing or not selectable |
| `bridge_unavailable` | Connection refused/lost or malformed protocol response |
| `authentication_failed` | Bridge rejected authentication |
| `tls_failed` | Required TLS or certificate verification failed |
| `stale_reference` | UIDVALIDITY changed; start a new search rather than reuse the UID or cursor |
| `message_not_found` | UID is missing or disappeared during retrieval |
| `timeout` | The 30-second operation deadline expired, including queue time |
| `protocol_failure` | Rejected command, unconfirmed read-only access, folder listing over 4,096 permitted folders, or unexpected internal failure |

Each completed/cancelled tool call emits a stderr diagnostic containing only operation name, random correlation ID, elapsed milliseconds, and fixed outcome. SDK/framework payload logging remains disabled. No folder, identifier, filters, cursor, mail, credentials, or exception text is logged.

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
| `2`, `configuration_username_invalid` or `configuration_allowlist_invalid` | Check the required username and legacy nonempty exact folder list |
| `2`, `configuration_folder_access_invalid` | Check the explicit mode, required corresponding list, and absence of conflicting fields or legacy settings; see [folder policies](CONFIGURATION.md#folder-access-policy) |
| `2`, `configuration_password_*`, `configuration_settings_*`, or `configuration_certificate_*` file/directory errors | Check existence, ownership, private modes, regular-file type, and symlinks for all three local files |
| `2`, `configuration_password_invalid` | Check UTF-8 password content and line endings without printing the secret |
| `2`, `configuration_public_certificate_required` or `configuration_certificate_invalid` | Check that the file contains a single public certificate and that its validity dates include the current time |
| `2`, `configuration_file_too_large` | Check the file limits in the configuration guide |
| `2`, `configuration_linux_required`, `configuration_metadata_unavailable`, or `configuration_path_unsafe` | Use Linux with working opened-handle checks; remove unsafe ancestor permissions, symlinks, and `.`/`..` path components |
| `2`, `configuration_invalid` or `configuration_invalid_or_unavailable` | Check JSON spelling/types, readability, certificate format, and password encoding locally |
| `1`, `host: failed` | The host failed; review the build/runtime setup and reproduce with synthetic fixtures |

All configuration diagnostics carry the `startup: ` prefix, including rows that abbreviate it. Failures intentionally omit raw exceptions and sensitive values. Share only sanitized codes when reporting problems. Restart after fixing external settings; the server does not reload them automatically.

## Live acceptance status

An operator-performed manual real Bridge smoke test succeeded on 2026-10-18 for MCP initialization, discovery of the three approved tools, folder listing, and bounded delivery-date-range search with truncation reporting. Bridge/Linux/runtime versions were not supplied for that run. Message-body retrieval, exact live date-boundary exclusion, before/after persistent flag and membership preservation, and failure/recovery behavior remain unverified. No mailbox output is retained in the documentation. Subsequent operator investigation found imported messages with old sender dates and newer internal dates. The original smoke test confirmed search execution, not the intended sender-date behavior. The replacement sender-date filters have not yet been tested against real Bridge.

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

Review the client's trust prompt and model/data policy before enabling tools. Use **MCP: List Servers** to start/stop/restart the process and inspect sanitized stderr. Discovery should show three base tools, plus `download_attachment` only when configured. Restart after changing configuration, credentials, or pins. A client tool call can access real mail; do not test it with personal settings before authorization. Never use `dotnet run` as the MCP transport command or merge stderr into stdout.

## Runtime troubleshooting

| Symptom | Safe operator action |
| --- | --- |
| `bridge_unavailable` | Start/unlock Bridge locally, wait for sync, check the displayed IMAP loopback endpoint and port, then retry deliberately. Initialization can succeed while Bridge is down. No automatic retries occur. |
| `authentication_failed` | Check Bridge's generated IMAP username/password locally. If changed, update only the external password/configuration and restart the host. Do not use the Proton account password or inspect keyrings/caches. |
| `tls_failed` | Check required mode, local clock, public certificate validity and intended Bridge instance. Follow [certificate replacement](LINUX-SETUP.md#export-and-replace-the-certificate); do not accept a network-learned pin or disable verification. |
| `denied_folder` or absent discovery entry | Check the configured mode and exact full-name allowlist/exclusions; missing/nonselectable entries are omitted. Parent rules do not apply to children. The server never creates or subscribes folders. |
| `stale_reference` | Search again and use the new folder + UIDVALIDITY + UID. During acceptance stop and investigate a changed epoch before preparing a new external manifest. |
| `message_not_found` | A message disappeared or the identity is wrong. Search again; never substitute sequence number or Message-ID. |
| `timeout` / connection lost | Check Bridge availability and competing calls. Each operation has 30 seconds including queue time. A later call uses a fresh connection. Send an MCP cancellation notification to cancel remotely. |
| Truncated search/body | Search: follow `nextCursor` with the same filters until null, at most 50 summaries per call. Body: the 32,000-character cap still applies. |
| `oversized`, `unsupported`, `missing`, `malformed` | Inspect status and metadata. Selected bodies over 1 MiB are not fetched; malformed or unavailable bodies stay metadata-only. Use the explicit configured download tool for attachments; `get_message` does not fetch attachment content or alternate bodies to evade limits. |
| `protocol_failure` / read-only rejection | If broad discovery exceeds 4,096 permitted folders, narrow the folder policy; no partial list is returned. For protocol/read-only failures, stop live testing and reproduce with synthetic fixtures. Do not enable SELECT/read-write, remove PEEK, or weaken TLS to work around Bridge behavior. |
| Live state comparison failed | Stop other clients/rules, inspect fixtures locally using read-only access, and investigate changes. Never reset the baseline silently or clean up flagged messages with this suite. |

Report only fixed codes, versions, and pass/fail outcomes. Do not enable production wire logging, paste message results, or attach raw settings to diagnose failures.
