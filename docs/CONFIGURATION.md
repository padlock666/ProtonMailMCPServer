# Configuration guide

The server loads one external JSON configuration file at startup. This guide owns settings, validation and protected-file requirements; [USAGE.md](USAGE.md) covers launch/tools and [PLAN.md](../PLAN.md) tracks acceptance.

Omitting `AttachmentDownloads` keeps downloads disabled and preserves the three base tools. Add the optional setting below to enable a fourth tool. The default runnable example keeps downloads disabled.

## Contents

- [File locations and loading](#file-locations-and-loading)
- [JSON settings](#json-settings)
- [Folder access policy](#folder-access-policy)
- [Protected local files](#protected-local-files)
- [Public certificate and TLS pin](#public-certificate-and-tls-pin)
- [Optional attachment downloads](#optional-attachment-downloads)
- [Configuration errors](#configuration-errors)
- [GNOME Keyring](#gnome-keyring)

## File locations and loading

Pass exactly `--config /absolute/path/to/config.json` after the application DLL. The configuration file and public certificate must use absolute paths. Keep real settings and credentials outside the checkout, preferably in a private XDG configuration directory. The application does not infer repository boundaries or relocate files for you.

Only this JSON file supplies application settings. `appsettings.json`, environment variables, and additional command-line overrides are not supported configuration sources. JSON property names and mode values are case-sensitive; unknown or duplicate properties fail startup. Settings and the public certificate are loaded once; restart after changing them. The keyring password is retrieved afresh after pinned TLS on each operation.

## JSON settings

Copy [config.example.json](../examples/config.example.json) to a private directory outside the checkout and edit that external copy. Set the directory to mode `0700` and the JSON file to `0600` or `0400`. The template below contains placeholders; replace the username and file paths locally, and set the port and TLS mode to match Bridge's displayed IMAP settings. Do not add real credentials or settings to the example in the repository.

```json
{
  "Host": "127.0.0.1",
  "Port": 1143,
  "TlsMode": "StartTls",
  "Username": "<bridge-imap-username>",
  "Keyring": { "Application": "ProtonMailMcp", "Account": "BrigePassword" },
  "CertificateFile": "/absolute/private/directory/bridge-public.pem",
  "FolderAccess": {
    "Mode": "Allowlist",
    "AllowedFolders": ["INBOX"]
  }
}
```

| Property | Required | Accepted value |
| --- | --- | --- |
| `Host` | No | Literal loopback IP; defaults to `127.0.0.1`. Canonical dotted-decimal IPv4 or IPv6 loopback such as `::1`; no hostnames, scoped addresses, non-loopback addresses, or alternate IPv4 spellings |
| `Port` | Yes | Integer from 1 through 65,535, copied from Bridge's IMAP settings |
| `TlsMode` | Yes | Exactly `StartTls` for mandatory STARTTLS or `SslOnConnect` for TLS from connection start, matching Bridge |
| `Username` | Yes | Nonblank Bridge IMAP username without control characters |
| `Keyring` | Yes | Object with required nonblank `Application` and `Account` strings, each at most 256 UTF-8 bytes without control characters; exact case-sensitive lookup attributes |
| `CertificateFile` | Yes | Absolute path to the exported public Bridge certificate |
| `FolderAccess` | Yes, unless using legacy `AllowedFolders` | Object with explicit `Mode` and the corresponding folder list; see below |
| `AttachmentDownloads` | No | Object with one required `Directory` absolute path; omission disables downloads; null/unknown fields fail startup |
| `AllowedFolders` | Legacy alternative | Top-level nonempty exact folder list; cannot be combined with `FolderAccess` |

Use Bridge-generated IMAP credentials, not the Proton account password. Read the values from the operator-managed Bridge settings; the server does not discover credentials, log in to Bridge on your behalf, or inspect Bridge's private stores. SMTP settings are not used. Do not infer TLS mode from the port.

## Folder access policy

Start with `FolderAccess.Mode` set to `Allowlist` and `AllowedFolders` set to a narrow list such as `["INBOX"]`, as in the complete example above. Missing or empty allowlists fail startup. New folders require a configuration update and restart before access is permitted.

To permit all Bridge folders and labels without enumerating them, replace the `FolderAccess` object with:

```json
{
  "Mode": "AllExcept",
  "ExcludedFolders": []
}
```

An empty exclusion list explicitly permits every selectable folder. To block particular folders, set `ExcludedFolders` to their exact full names, for example `["Labels/Private", "Trash"]`. `AllExcept` discovers folders afresh on each listing, includes unsubscribed folders, and automatically permits newly created folders and labels. It does not change subscriptions. Search and read still target one folder per call.

Folder listing returns at most 4,096 permitted folders, each with a usable full name of at most 1,024 characters. A larger permitted set fails with `protocol_failure` and returns no partial list. Direct search/read still follow the policy; narrow the policy to reduce the discovery result. Namespace metadata parsing remains subject to MailKit's memory use and the existing operation deadline.

**Exclusions block access through a folder, not access to every message carrying that label.** The same message can remain accessible through another permitted folder or label, including INBOX or All Mail. This feature does not implement message-level label filtering.

Both lists accept at most 256 entries, each at most 1,024 characters. Null/blank entries, control characters, and wildcard characters `*` and `%` are rejected. Names match exactly, including case, except that `INBOX` is case-insensitive. Allowing or excluding a parent folder has no effect on its descendants: excluding `Labels/Private` still permits `Labels/Private/Child` in `AllExcept` mode. Use full folder names as exposed by Bridge. Missing and nonselectable folders are omitted from discovery, not created or repaired. Search and direct retrieval check the policy before connecting and again against the resolved full folder name before opening it.

`Mode` must be exactly `Allowlist` or `AllExcept`. The corresponding list is required, even when `ExcludedFolders` is empty. Supplying `ExcludedFolders` in `Allowlist` mode or `AllowedFolders` in `AllExcept` mode fails startup, including null or empty values. Missing/unknown modes and unknown fields also fail startup; no policy falls back to all folders.

Existing configurations with only top-level `"AllowedFolders": ["INBOX"]` remain supported and use allowlist mode. To migrate, remove that top-level property and add the `FolderAccess` object. Combining the formats fails startup, even if the top-level list is null. The live acceptance suite requires allowlist mode with exactly one dedicated test folder; it rejects `AllExcept`.

## Protected local files

Store the Bridge-generated IMAP password using [secret-tool](LINUX-SETUP.md#gnome-keyring-setup). Keep passwords out of JSON, launch arguments, environment examples, repository files, logs, and chat.

The JSON configuration and public certificate must each be:

- A regular, readable file owned by the effective user running the server.
- Private, with no group/other or special permission bits; `0600` or `0400` are typical modes.
- Inside an immediate parent directory owned by that user and private, normally `0700`.

The JSON configuration and certificate are each capped at 65,536 bytes.

Symlinks are rejected at every path component. Ancestor directories must be owned by the user or root and not writable by group/others; a root-owned sticky directory such as `/tmp` is accepted. `.` and `..` path components are rejected. Validation walks directories through open handles and checks the opened files and directories using Linux `statx`; unavailable metadata fails closed. The server does not create or repair these files or their permissions.

## Public certificate and TLS pin

Supply one public X.509 certificate as PEM or DER. It must be within its validity period. Private-key bundles, multiple PEM certificates, and PKCS#12 files are rejected. Export and verify the public certificate using the operator-managed Bridge installation; the server never needs Bridge's private key. Although its contents are public, its file permissions must protect the pin from replacement.

Each mailbox connection requires encrypted IMAP and an exact match to the configured certificate before authentication. A self-signed certificate can be trusted through this explicit pin; a different certificate fails even if a system certificate authority trusts it. There is no plaintext fallback, trust-on-first-use, or automatic pin replacement.

When Bridge's certificate changes, verify the change independently, export the replacement public certificate, update the external file or `CertificateFile` path, and restart the server. Do not disable verification to resolve a mismatch. Acceptance evidence is tracked in [PLAN.md](../PLAN.md).

## Optional attachment downloads

To enable `download_attachment`, create a dedicated empty directory locally outside the checkout and settings directories, set mode `0700`, and add this fragment to the external JSON settings:

```json
{
  "AttachmentDownloads": {
    "Directory": "/absolute/private/directory/attachments"
  }
}
```

Before a download, the directory must already exist, be owned by the server's effective user and have exactly mode `0700`. Its path must be absolute, control-free, at most 3,000 UTF-8 bytes, with no symlink components, `.`/`..`, doubled or trailing slashes; `/` is invalid. Ancestors follow the protected-path rules above. A directory containing the loaded settings/certificate files is rejected. Startup checks path syntax and protected-file separation without opening or locking the directory. Multiple server instances can initialize with the same configured storage. Each download validates handles, waits for an exclusive cross-process lock within its existing 30-second deadline, rechecks quota and holds the lock until publication or failure cleanup completes. Missing or unsafe storage fails only the download; base tools remain available. Repairing storage does not require restarting the server. Downloading requires 64-bit Linux and a supported local filesystem; do not use network storage. It never creates or repairs the directory.

Restart after adding/removing/changing this object. Unknown/duplicate properties, null objects and absent/null directory values fail startup; omission is the only disabled form. There are no environment or caller overrides and no default download directory.

Downloads use generated filenames and private `0600` files. Fixed [transfer/directory quotas](ATTACHMENT-DOWNLOADS.md#limits-and-retention) include incomplete files and reserve capacity conservatively for a full-size download. Completed files remain until you remove them; stop the server before clearing abandoned `.part` files. No automatic expiry, cleanup of completed files or overwrite is provided. Keep plaintext out of source control and unintended backup/sync locations. See [the download contract](ATTACHMENT-DOWNLOADS.md) for retention, commit/cancellation and quota details.

Malformed download configuration uses `configuration_attachment_downloads_invalid`; placing protected settings/certificate files under the download path uses `configuration_download_directory_unsafe` at startup. Runtime storage validation returns `download_directory_unsafe`, non-contention locking failures return `download_storage_failed`, and lock-wait deadline/cancellation retains the ordinary timeout/cancellation outcome. Errors omit paths. `configuration_download_directory_in_use` is no longer a startup outcome. The client must be able to read the returned host path; a cloud sandbox does not automatically share this directory.

## Configuration errors

Configuration failures exit with code `2` and a fixed `startup: configuration_...` diagnostic on stderr. Diagnostics omit paths, settings, credentials, and underlying exception details. Common codes and next checks are listed in the [usage guide](USAGE.md#startup-diagnostics).

See [SECURITY.md](../SECURITY.md) for the full trust boundary and [TESTING.md](TESTING.md) for synthetic verification and the implemented opt-in live acceptance procedure.

For the complete installation, public-certificate export/replacement, and operator preparation workflow, see [LINUX-SETUP.md](LINUX-SETUP.md). Live acceptance uses a separate external manifest and explicit opt-in; these are test-runner inputs, never additional application settings.

## GNOME Keyring

`Keyring` is required. Its `Application` and `Account` values map directly to the case-sensitive `application` and `account` attributes used by `secret-tool`. They are nonsecret selectors; a display label does not select an item. Missing/null objects, missing/null/blank/control-containing/oversized attributes, unknown fields and duplicate properties fail startup. Selector validation uses `configuration_keyring_invalid`; type/JSON failures use sanitized configuration errors.

The server calls libsecret directly in the launching user's desktop session. It searches collections using these attributes and may show a desktop unlock prompt. Keep the application/account pair unique across collections: libsecret chooses a retrievable matching item. Provision only a dedicated MCP item; leave Bridge-owned entries alone.

Passwords must be strict UTF-8 text of 1–4,096 bytes, nonblank and without NUL or control characters, including CR/LF. Surrounding spaces are preserved. Native dependencies load only when a mailbox call reaches credential lookup; initialization and tool discovery need no session bus. The inherited bus must use one explicit local Unix transport. Leave `SECRET_SERVICE_BUS_NAME`, `G_DBUS_DEBUG` and `G_MESSAGES_DEBUG` unset to prevent service redirection or native debug disclosure.

Follow [GNOME Keyring setup](LINUX-SETUP.md#gnome-keyring-setup) for prerequisites and the exact `secret-tool store` command. Password rotation is observed on the next operation. Selector, username, certificate and other settings changes require restart. [KEYRING.md](KEYRING.md) describes cancellation, resource ownership and trust limits.
