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
3. Create a private directory outside the checkout, for example `$HOME/.config/protonmail-mcp`, with mode `0700`. Copy [config.example.json](../examples/config.example.json) there and edit the external copy. Set the JSON and exported public certificate to mode `0600` or `0400`, owned by your user. Store the credential using [GNOME Keyring setup](#gnome-keyring-setup). Follow all [configuration requirements](CONFIGURATION.md).
4. Export and verify the public certificate as described below. Set an absolute path for the public certificate in the external JSON. Set `StartTls` for Bridge STARTTLS or `SslOnConnect` for Bridge SSL; use the displayed port rather than assuming its default. Proton documents connection modes in its [settings guide](https://proton.me/support/comprehensive-guide-to-bridge-settings).
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

The credential is Bridge's generated **IMAP password**. Use the same normal Linux user and desktop session for provisioning and the MCP client. Use `sudo` only to install packages.

### 1. Install the prerequisites

On Debian/Ubuntu and derivatives:

```bash
sudo apt update
sudo apt install gnome-keyring libpam-gnome-keyring libsecret-1-0 libsecret-tools seahorse
```

`libsecret-tools` supplies `secret-tool`. The server calls libsecret directly; it does not launch a credential command. Log out and log back in after installing desktop integration. In a desktop terminal, check the service without retrieving a secret:

```bash
busctl --user status org.freedesktop.secrets
```

The owner should be `gnome-keyring-daemon`. Open **Passwords and Keys** (`seahorse`) if service activation is needed. Do not start a competing provider on your desktop bus. Other providers can answer the same API; this check is an operator responsibility.

### 2. Prepare a password-protected keyring

Open **Passwords and Keys**, find **Login** under Passwords, and unlock it. If needed, create a password keyring with a strong, nonempty password and choose **Set as default**. `secret-tool store` writes to that default collection. GNOME documents [creating a keyring](https://help.gnome.org/seahorse/keyring-create.html) and [changing the default](https://help.gnome.org/seahorse/keyring-change-default.html).

For ordinary automatic unlock, the Login keyring password must match your login password and GNOME Keyring PAM integration must be enabled. Automatic or fingerprint login may leave it locked. Unlock it through the desktop; never set an empty keyring password to suppress prompts. Headless, SSH, container and system-service sessions usually lack the required desktop bus or unlock UI.

### 3. Add the Bridge credential with secret-tool

Run this exact command in your desktop terminal:

```bash
secret-tool store --label='Proton Mail MCP' \
  application ProtonMailMcp account BrigePassword
```

At the hidden **Password:** prompt, enter the **Bridge-generated IMAP password**, then press Enter. A graphical keyring unlock prompt may also appear. Use neither the Proton account password nor the keyring-unlock password. Do not put the IMAP password in command arguments, pipes, environment variables, scripts, or shell history.

The label is for humans. The application selects by the exact `application` and `account` attributes. Keep this pair unique across collections: libsecret can choose one of several matches. Provision a dedicated MCP item and leave Bridge's internal entries alone. Creating an item only by label in Seahorse may not set the required attributes.

### 4. Configure the selectors

In your private external copy of [config.example.json](../examples/config.example.json), set:

```json
{
  "Keyring": {
    "Application": "ProtonMailMcp",
    "Account": "BrigePassword"
  }
}
```

This is a fragment. Also set the Bridge IMAP username, loopback endpoint, port, TLS mode, independently exported public certificate and folder policy. The selectors above must match the store command exactly. To choose different identifiers, change both the command's attribute values and the JSON. Selectors are not secrets or access controls.

### 5. Restart and use the server

Restart the launching MCP client after settings changes. Keep `--config /absolute/path/to/config.json` as the launch arguments. The client must run in the intended desktop session so its child inherits the local Unix session bus. Leave `SECRET_SERVICE_BUS_NAME`, `G_DBUS_DEBUG` and `G_MESSAGES_DEBUG` unset.

Tool discovery performs no keyring lookup or Bridge connection. A mailbox call verifies Bridge TLS, then retrieves the password; approve any GNOME unlock dialog on the desktop within the operation's 30-second deadline. Dismissing a dialog can leave no retrievable secret. For missing credentials, check the exact attributes and desktop user/session. For `credential_unavailable`, check native libraries and the session bus. See [runtime guidance](USAGE.md#keyring-behavior).

To rotate the credential, repeat the same `secret-tool store` command and enter the new Bridge IMAP password at its hidden prompt. It updates a matching item in the default collection. The next mailbox operation retrieves the new value. Inspect duplicate items deliberately through Passwords and Keys if necessary. Full live acceptance remains a separate [authorized procedure](TESTING.md#opt-in-real-bridge-acceptance).
