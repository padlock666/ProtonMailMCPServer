# Configuration guide

The server loads one external JSON configuration file at startup. This guide describes the implemented configuration contract; see the [usage guide](USAGE.md) for building and launching the stdio host and the three implemented tool contracts. Application configuration is unchanged in Phase 4; live Bridge acceptance remains pending in [PLAN.md](../PLAN.md).

## File locations and loading

Pass exactly `--config /absolute/path/to/config.json` after the application DLL. The configuration file, password file, and public certificate must use absolute paths. Keep real settings and credentials outside the checkout, preferably in a private XDG configuration directory. The application does not infer repository boundaries or relocate files for you.

Only this JSON file supplies application settings. `appsettings.json`, environment variables, and additional command-line overrides are not supported configuration sources. JSON property names and TLS mode values are case-sensitive; unknown properties fail startup. Settings, the password, and the public certificate are loaded once; restart the server after changing them.

## JSON settings

Copy [config.example.json](../examples/config.example.json) to a private directory outside the checkout and edit that external copy. Set the directory to mode `0700` and the JSON file to `0600` or `0400`. The template below contains placeholders; replace the username and file paths locally, and set the port and TLS mode to match Bridge's displayed IMAP settings. Do not add real credentials or settings to the example in the repository.

```json
{
  "Host": "127.0.0.1",
  "Port": 1143,
  "TlsMode": "StartTls",
  "Username": "<bridge-imap-username>",
  "PasswordFile": "/absolute/private/directory/bridge.password",
  "CertificateFile": "/absolute/private/directory/bridge-public.pem",
  "AllowedFolders": ["INBOX"]
}
```

| Property | Required | Accepted value |
| --- | --- | --- |
| `Host` | No | Literal loopback IP; defaults to `127.0.0.1`. Canonical dotted-decimal IPv4 or IPv6 loopback such as `::1`; no hostnames, scoped addresses, non-loopback addresses, or alternate IPv4 spellings |
| `Port` | Yes | Integer from 1 through 65,535, copied from Bridge's IMAP settings |
| `TlsMode` | Yes | Exactly `StartTls` for mandatory STARTTLS or `SslOnConnect` for TLS from connection start, matching Bridge |
| `Username` | Yes | Nonblank Bridge IMAP username without control characters |
| `PasswordFile` | Yes | Absolute path to the separate owner-only file containing the Bridge-generated IMAP password |
| `CertificateFile` | Yes | Absolute path to the exported public Bridge certificate |
| `AllowedFolders` | Yes | Nonempty array of exact folder full names; see below |

Use Bridge-generated IMAP credentials, not the Proton account password. Read the values from the operator-managed Bridge settings; the server does not discover credentials, log in to Bridge on your behalf, or inspect Bridge's private stores. SMTP settings are not used. Do not infer TLS mode from the port.

## Folder allowlist

Start with a narrow list such as `["INBOX"]`. The list accepts at most 256 entries, each at most 1,024 characters. Blank entries, control characters, and wildcard characters `*` and `%` are rejected. Empty lists fail closed.

Names match exactly, including case, except that `INBOX` is case-insensitive. Allowing a parent folder does not allow its descendants. Use full folder names as exposed by Bridge; missing and nonselectable folders are omitted from discovery, not created or repaired. Search and direct message retrieval both recheck the allowlist.

## Protected local files

Create and populate the password file locally using a trusted editor or another method that keeps the secret out of shell history and process arguments. Do not put passwords in JSON, launch arguments, environment examples, repository files, logs, or chat.

The JSON configuration, public certificate, and password file must each be:

- A regular, readable file owned by the effective user running the server.
- Private, with no group/other or special permission bits; `0600` or `0400` are typical modes.
- Inside an immediate parent directory owned by that user and private, normally `0700`.

The password must be nonempty, at most 4,096 bytes, and valid UTF-8. A conventional terminal LF or CRLF is accepted; surrounding password spaces are preserved. Blank passwords and embedded control characters are rejected. The JSON configuration and certificate are each capped at 65,536 bytes.

Symlinks are rejected at every path component. Ancestor directories must be owned by the user or root and not writable by group/others; a root-owned sticky directory such as `/tmp` is accepted. `.` and `..` path components are rejected. Validation walks directories through open handles and checks the opened files and directories using Linux `statx`; unavailable metadata fails closed. The server does not create or repair these files or their permissions.

## Public certificate and TLS pin

Supply one public X.509 certificate as PEM or DER. It must be within its validity period. Private-key bundles, multiple PEM certificates, and PKCS#12 files are rejected. Export and verify the public certificate using the operator-managed Bridge installation; the server never needs Bridge's private key. Although its contents are public, its file permissions must protect the pin from replacement.

Each mailbox connection requires encrypted IMAP and an exact match to the configured certificate before authentication. A self-signed certificate can be trusted through this explicit pin; a different certificate fails even if a system certificate authority trusts it. There is no plaintext fallback, trust-on-first-use, or automatic pin replacement.

When Bridge's certificate changes, verify the change independently, export the replacement public certificate, update the external file or `CertificateFile` path, and restart the server. Do not disable verification to resolve a mismatch. These controls have synthetic protocol evidence; real Bridge compatibility remains unverified.

## Configuration errors

Configuration failures exit with code `2` and a fixed `startup: configuration_...` diagnostic on stderr. Diagnostics omit paths, settings, credentials, and underlying exception details. Common codes and next checks are listed in the [usage guide](USAGE.md#startup-diagnostics).

See [SECURITY.md](../SECURITY.md) for the full trust boundary and [TESTING.md](TESTING.md) for synthetic verification and the implemented opt-in live acceptance procedure.

For the complete installation, public-certificate export/replacement, and operator preparation workflow, see [LINUX-SETUP.md](LINUX-SETUP.md). Live acceptance uses a separate external manifest and explicit opt-in; these are test-runner inputs, never additional application settings.
