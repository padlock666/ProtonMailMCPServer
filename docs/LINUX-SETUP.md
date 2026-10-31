# Linux and Bridge handoff

Prepare Bridge and local files using this guide, then follow [USAGE.md](USAGE.md) to launch the server. Full live acceptance remains pending in [PLAN.md](../PLAN.md); the synthetic suite needs no Bridge installation.

## Contents

- [Prepare Bridge and external files](#prepare-bridge-and-external-files)
- [Export and replace the certificate](#export-and-replace-the-certificate)
- [Live acceptance preparation](#live-acceptance-preparation)
- [GNOME Keyring setup](#gnome-keyring-setup)

## Prepare Bridge and external files

1. Install the pinned .NET SDK from `global.json` and verify `dotnet --info`. Install Bridge using Proton's current [Linux installation instructions](https://proton.me/support/bridge-for-linux), including the credential-store prerequisites for your desktop. Record your distribution/release and Bridge version locally. Version requirements can change; installation support is distinct from this project's tested compatibility.
2. Start Bridge yourself, sign in through its own UI, and wait for synchronization. Read the displayed IMAP username, generated password, port, and connection mode. Use a literal loopback address. Do not supply the Proton account password or any SMTP settings.
3. Create a private directory outside the checkout, for example `$HOME/.config/protonmail-mcp`, with mode `0700`. Choose either [file mode](../examples/config.example.json) or [GNOME Keyring mode](#gnome-keyring-setup), copy the appropriate example there and edit the external copy. Set the JSON, exported public certificate, and any selected password file to mode `0600` or `0400` and keep them owned by your user. File mode uses a trusted local editor for its password file; keyring mode uses the hidden-input provisioning recipe below. Keep secrets out of command arguments and shell history. Follow all [configuration requirements](CONFIGURATION.md).
4. Export and verify the public certificate as described below. Set an absolute path for the public certificate and, in file mode, the password file in the external JSON. Set `StartTls` for Bridge STARTTLS or `SslOnConnect` for Bridge SSL; use the displayed port rather than assuming its default. Proton documents connection modes in its [settings guide](https://proton.me/support/comprehensive-guide-to-bridge-settings).
5. Start ordinary use with a narrow allowlist such as `INBOX`, or explicitly choose `AllExcept` for broad folder/label access as described in [configuration](CONFIGURATION.md#folder-access-policy). For acceptance, use a separate configuration whose sole allowed folder is the dedicated operator-prepared test folder; never use INBOX as the acceptance folder. Configure its exact Bridge full name. No automatic descendants are allowed.
6. Build and run the [synthetic checks](DEVELOPMENT.md#build-and-test-workflow). Configure a trusted stdio client using [USAGE.md](USAGE.md#launch-over-stdio). Decide locally whether that client may send decrypted mail to a hosted model. Initialization/discovery alone do not access mail; tool calls do.

Do not send configuration contents, credentials, fixture identities, message data, or protocol logs to chat. The operator supplies paths and authorizes the specific live stages separately. The server never reads Bridge stores, keyrings, caches, or account databases to discover settings.

## Export and replace the certificate

In Bridge settings, use **Advanced settings → Export TLS certificates**, following Proton's [export documentation](https://proton.me/support/comprehensive-guide-to-bridge-settings). The export action can produce both a public certificate and a private key. Export into an operator-controlled private directory outside this checkout; supply only the single public PEM/DER certificate to this application. Keep the exported private key separate and manage it locally; never attach it or configure it here.

Inspect the operator-exported public file locally, for example:

```bash
openssl x509 -in /absolute/private/directory/bridge-public.pem -noout -dates -fingerprint -sha256
```

For DER add `-inform DER`. Verify the file came from the intended Bridge instance, review its validity dates, and compare its fingerprint with a previously recorded trusted fingerprint when available. This inspection reads an exported file; it does not establish trust by probing a network peer. Do not use a certificate captured from an unauthenticated socket as an automatic replacement pin. System-wide certificate trust installation is unnecessary for this application.

When the peer changes, stop the MCP process, independently confirm the reason in the operator-managed Bridge installation, re-export its public certificate, review the replacement locally, update `CertificateFile` or its contents, and restart the MCP process. Re-run explicitly authorized acceptance stages. Expired or future certificates also fail closed; verify the system clock and certificate dates. Never disable pinning, enable plaintext, accept all certificates, or use read/write selection to resolve a compatibility failure.

## Live acceptance preparation

Follow the [exact fixture schema and stages](TESTING.md#opt-in-real-bridge-acceptance) to prepare the dedicated folder through a separate trusted client, record known identities/text hashes, pause other writers and authorize specific runs. This server and its suite never prepare or clean up live messages. Record actual versions, outcomes and unsupported states in [PLAN.md](../PLAN.md) without sensitive values.

## GNOME Keyring setup

This option stores the Bridge-generated IMAP password in a dedicated GNOME Keyring item. The external JSON holds its selector UUID; the server retrieves the password after pinned TLS on each mailbox operation. Use the same Linux user and desktop session for provisioning and for the MCP client that launches the server. Run provisioning and the server as your normal user.

### 1. Install the prerequisites

The runtime needs GNOME Keyring and libsecret/GLib/GIO/GObject. The provisioning recipe also needs Python 3, PyGObject and the Secret 1 typelib; Passwords and Keys (`seahorse`) provides the collection-management UI. On Ubuntu 24.04 and its derivatives, the operator can install these packages with:

```bash
sudo apt install gnome-keyring libsecret-1-0 python3-gi gir1.2-secret-1 seahorse
```

These are distribution packages, including [the Secret typelib](https://packages.ubuntu.com/noble/gir1.2-secret-1) and [Seahorse](https://packages.ubuntu.com/noble/seahorse). On other distributions, use their package equivalents. Installation is an operator action; the server does not install packages, launch a keyring daemon or unlock collections. [Tested versions and trust limits](KEYRING.md#native-dependencies-and-apis) describe the isolated compatibility evidence. Python is used only for provisioning, not by the running server.

### 2. Prepare the default collection

Open **Passwords and Keys** from the desktop application menu, or run `seahorse` in a desktop terminal. Under **Passwords**, use the intended default keyring, usually **Login**. Unlock it through its context menu or lock icon and enter that collection's password. GNOME documents [unlocking a keyring](https://help.gnome.org/seahorse/keyring-unlock.html) and [choosing the default keyring](https://help.gnome.org/seahorse/keyring-change-default.html).

If you need a new collection, use **+ → Password Keyring**, give it a name and a nonempty collection password, then deliberately choose **Set as default** and unlock it. See GNOME's [creation instructions](https://help.gnome.org/seahorse/keyring-create.html). Changing the default also changes where other applications may store future passwords. An existing suitable default needs no change.

`CollectionAlias` in this server's configuration must remain `default`, even when the collection is displayed as **Login** or another name. The alias points to the selected collection. The server fails with `credential_locked` while it is locked; unlock it through the desktop before using mailbox tools. Automatic unlock for headless, SSH or system-service deployments is outside this feature.

### 3. Store the dedicated MCP password

Read the generated **IMAP password** from Bridge's own settings. Keep the Bridge IMAP username for the JSON configuration. The Proton account password and the collection-unlock password serve different purposes and must not be entered as the IMAP secret. Leave Bridge-owned keyring entries alone.

Create a private directory outside the checkout if you do not already have one:

```bash
mkdir -p "$HOME/.config/protonmail-mcp"
chmod 700 "$HOME/.config/protonmail-mcp"
```

Save the Python code from [the provisioning recipe](KEYRING.md#gnome-keyring-provisioning-and-migration) as `provision-keyring.py` in that directory using a trusted editor. The script contains code only; enter the password at its hidden terminal prompt. Run it from an interactive terminal:

```bash
chmod 600 "$HOME/.config/protonmail-mcp/provision-keyring.py"
/usr/bin/python3 "$HOME/.config/protonmail-mcp/provision-keyring.py"
```

At `Bridge IMAP password:`, enter the Bridge-generated IMAP password. Do not place it in a shell command, environment variable, JSON file or script. On success the script prints a UUID. Keep that UUID for the next step; it is a nonsecret selector, not the password.

The script creates one item in the unlocked default collection with all required attributes:

| Attribute | Value |
| --- | --- |
| `application` | `protonmail-mcp` |
| `purpose` | `bridge-imap-password` |
| `id` | The UUID printed by the script |

Its display label is **Proton Mail MCP credential**. Selection depends on the attributes, so use the recipe to populate them. The server requires exactly one item matching the full selector, including locked matches, and never chooses an item by label or username.

### 4. Select the item in the external configuration

For a new setup, use [config.keyring.example.json](../examples/config.keyring.example.json) as the complete configuration template and save your edited copy outside the checkout. For an existing setup, edit your current external JSON to preserve its endpoint, public certificate, folder policy and optional download settings. Remove `PasswordFile` entirely, including any null value, and add:

```json
{
  "PasswordKeyring": {
    "CollectionAlias": "default",
    "ItemId": "44ec5121-59ce-4c39-8b45-551823feb631"
  }
}
```

This is a fragment. Replace the synthetic `ItemId` with the UUID your script printed. Keep the exact case and field names. Configure Bridge's IMAP username, port and TLS mode, plus the absolute path to its independently exported public certificate, as described in [configuration](CONFIGURATION.md). No password file is needed in keyring mode.

Keep the external configuration and public certificate owned by your user, with mode `0600` or `0400`, in a private parent directory such as the one above. All existing [protected-file requirements](CONFIGURATION.md#protected-local-files) still apply. Exactly one password source is permitted; there is no file fallback.

### 5. Restart and check the setup

Restart the MCP server through its launching client after changing the JSON. Keep the existing `--config /absolute/path/to/config.json` launch arguments; keyring mode adds no launch argument. Launch the client in the prepared desktop session so the child server inherits its local Unix session-bus context. Leave `SECRET_SERVICE_BUS_NAME`, `G_DBUS_DEBUG` and `G_MESSAGES_DEBUG` unset, and keep personal bus addresses out of tracked launch files.

Refresh tool discovery to confirm startup and the expected three or four tools. Discovery does not query the keyring or authenticate to Bridge. When you deliberately invoke a mailbox tool such as `list_folders`, the server first verifies Bridge TLS and then looks up the selected item. Full live regression still requires the separate [operator-prepared acceptance procedure](TESTING.md#opt-in-real-bridge-acceptance).

For `credential_locked`, unlock the default collection in Passwords and Keys. For `credential_not_found`, check the default collection and the UUID in your external JSON. For `credential_ambiguous`, resolve duplicate matching MCP items through a trusted keyring tool. For `credential_unavailable`, check installed runtime libraries and the launching client's desktop-session context. The [runtime error guide](USAGE.md#keyring-behavior) covers other fixed failures without requiring secret values in diagnostics.

To rotate the password, update only your dedicated MCP item's secret through a trusted keyring tool, preserving its attributes. The next mailbox operation retrieves the new value without restarting. If you provision a new item instead, use its new UUID in the external JSON and restart. After separately verifying migration, you may retire an old password file and its backups under your own retention policy; the server never removes them.
