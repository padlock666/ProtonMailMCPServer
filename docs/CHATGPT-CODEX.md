# Connect ChatGPT and Codex

Set up a personal local plugin so you can select **Proton Mail Local** with `@` in ChatGPT Work or Codex desktop. The operator confirmed installation and Work tool visibility with this packaging on **2026-10-25**. Actual mailbox calls and client access to downloaded files remain pending in [PLAN.md](../PLAN.md).

## Contents

- [Prepare the server](#prepare-the-server)
- [Create the plugin files](#create-the-plugin-files)
- [Register the personal marketplace](#register-the-personal-marketplace)
- [Install and use the plugin](#install-and-use-the-plugin)
- [Verify the available tools](#verify-the-available-tools)
- [Alternative: direct local MCP connection](#alternative-direct-local-mcp-connection)
- [Hosted ChatGPT web](#hosted-chatgpt-web)

## Prepare the server

Run the client executor and Proton Mail Bridge on the same Linux machine. Follow [Linux setup](LINUX-SETUP.md), [configuration](CONFIGURATION.md), and the [build/verification commands](DEVELOPMENT.md#build-and-test-workflow) to prepare the Release DLL and an external private configuration file.

Use absolute paths in the examples below. Keep credentials outside the checkout and plugin package; only the configuration file path belongs in launch settings. Launch the built DLL with `dotnet`, and keep stderr separate from MCP stdout. Review the [privacy boundary](../SECURITY.md): the client can send decrypted tool results to its model provider.

Use the plugin as the sole Proton Mail registration. If you previously registered it directly, back up `~/.codex/config.toml` and remove the entire `[mcp_servers.protonmail]` table. Do not leave a same-name direct table with `enabled = false`, because it can mask the plugin connection.

If attachment downloads are enabled, multiple server instances can share the configured private directory. Downloads serialize across processes within the operation deadline; idle instances do not hold the directory lock. Missing/unsafe/busy storage does not prevent initialization or use of the three base tools. Restart clients after upgrading from the older lifetime-lock implementation so existing server processes release their locks. Downloads remain optional; omitting `AttachmentDownloads` exposes the three base tools.

## Create the plugin files

Create this personal plugin directory outside the checkout:

```text
~/.codex/plugins/protonmail-local/
  plugin.json
  mcp.json
  .codex-plugin/
    plugin.json
  .mcp.json
```

Include all four files to reproduce the packaging verified in Work. The portable files and compatibility files describe the same plugin and launch command. OpenAI documents [portable plugin packaging](https://developers.openai.com/plugins/build/plugins) and the [Codex compatibility format](https://developers.openai.com/api/docs/guides/agents-api/tools/plugins).

Create `plugin.json`:

```json
{
  "$schema": "https://agent-plugins.org/schemas/1.0.0/plugin.schema.json",
  "name": "protonmail-local",
  "version": "0.1.0",
  "description": "Read permitted Proton Mail folders through local Bridge.",
  "extensions": {
    "com.openai": {
      "requires_local_executor": true,
      "interface": {
        "displayName": "Proton Mail Local",
        "shortDescription": "Read mail through local Proton Mail Bridge"
      }
    }
  }
}
```

Create `mcp.json`, replacing the two absolute paths locally:

```json
{
  "$schema": "https://agent-plugins.org/schemas/1.0.0/mcp.schema.json",
  "mcpServers": {
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

Create `.codex-plugin/plugin.json`. Its `mcpServers` path resolves from the plugin root:

```json
{
  "name": "protonmail-local",
  "version": "0.1.0",
  "description": "Read permitted Proton Mail folders through local Bridge.",
  "requires_local_executor": true,
  "interface": {
    "displayName": "Proton Mail Local",
    "shortDescription": "Read mail through local Proton Mail Bridge"
  },
  "mcpServers": "./.mcp.json"
}
```

Create `.mcp.json` with the same absolute paths as `mcp.json`:

```json
{
  "mcpServers": {
    "protonmail": {
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

Keep the plugin identity/version and both launch declarations consistent when making changes. This personal package points to your existing build; moving or removing that build requires updating both MCP files.

## Register the personal marketplace

Create `~/.agents/plugins/marketplace.json`, or merge the plugin entry into an existing marketplace without replacing other entries:

```json
{
  "name": "personal-mail",
  "interface": { "displayName": "Personal Mail" },
  "plugins": [
    {
      "name": "protonmail-local",
      "source": {
        "source": "local",
        "path": "./.codex/plugins/protonmail-local"
      },
      "policy": {
        "installation": "AVAILABLE",
        "authentication": "ON_INSTALL"
      },
      "category": "Productivity"
    }
  ]
}
```

The plugin entry's `name` must match both manifests. The personal marketplace's `source.path` resolves from your home directory, so this example points to `~/.codex/plugins/protonmail-local`. **Personal Mail** is the marketplace display name; **Proton Mail Local** is the plugin display name. See [OpenAI's local marketplace instructions](https://developers.openai.com/plugins/build/plugins#install-a-local-plugin-manually).

## Install and use the plugin

1. Restart ChatGPT desktop to discover the marketplace.
2. Open the Plugins Directory, select **Personal Mail**, and install/enable **Proton Mail Local**.
3. Start a new local Work or Codex desktop chat.
4. Type `@`, search for **Proton Mail Local**, and select the plugin entry.
5. Ask for the available Proton Mail tool names without calling them.

This is the [plugin selection workflow](https://developers.openai.com/plugins/quickstart). Local stdio plugins require local execution; see [desktop-only plugins](https://developers.openai.com/plugins/deploy/desktop-only).

After editing plugin files, refresh the installed copy and restart ChatGPT before opening a new chat. For the marketplace and plugin names in this example, the CLI refresh command is:

```bash
codex plugin add protonmail-local@personal-mail
```

Installing the plugin supplies its MCP connection. No additional direct MCP registration is required. The marketplace's authentication policy does not replace the server's Bridge credentials or certificate pin.

## Verify the available tools

Expect these tools:

- `list_folders`
- `search_messages`
- `get_message`
- `download_attachment`, only when configured

Initialization and tool discovery validate local settings without connecting to Bridge. Seeing the tools confirms client wiring, not mailbox connectivity or live read-only acceptance.

For authorized live verification, follow the [operator-prepared acceptance procedure](TESTING.md#opt-in-real-bridge-acceptance). A request such as “List the permitted folders” accesses Bridge. Keep mailbox content and credentials out of setup conversations and evidence records.

Attachment downloads return private Linux file paths. The client must be able to read that filesystem; plugin installation does not upload those files or establish PDF interpretation. See the [download contract](ATTACHMENT-DOWNLOADS.md).

## Alternative: direct local MCP connection

If you do not need a plugin selectable with `@`, register the server directly instead:

```bash
codex mcp add protonmail -- dotnet \
  /absolute/path/to/checkout/src/ProtonMailMcp/bin/Release/net10.0/ProtonMailMcp.dll \
  --config /absolute/private/directory/config.json
```

This creates a direct MCP entry in Codex configuration. Use this route with the Proton Mail plugin disabled. See OpenAI's [MCP configuration guide](https://learn.chatgpt.com/docs/extend/mcp).

## Hosted ChatGPT web

This server supports local stdio only. Hosted ChatGPT web requires a remote MCP integration; local plugin installation does not provide one. An authenticated HTTP gateway or transport change requires a separate scope/security decision under [DESIGN.md](../DESIGN.md). Never expose Bridge itself. See [OpenAI's custom MCP connection guide](https://developers.openai.com/api/docs/guides/custom-mcp-server).
