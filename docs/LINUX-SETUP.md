# Linux and Bridge handoff

Phase 4 tooling is implemented. An operator-performed real Bridge folder-listing and date-range search smoke test succeeded on 2026-10-18; versions were not supplied for that run, and no Linux/Bridge combination has full live acceptance evidence yet. Follow [TESTING.md](TESTING.md#opt-in-real-bridge-acceptance) before claiming compatibility. The synthetic suite needs no Bridge installation. That test used the former internal-date filter; sender-date filtering now replaces it and awaits live verification. See [usage](USAGE.md) for the updated inputs.

## Prepare Bridge and external files

1. Install the pinned .NET SDK from `global.json` and verify `dotnet --info`. Install Bridge using Proton's current [Linux installation instructions](https://proton.me/support/bridge-for-linux), including the credential-store prerequisites for your desktop. Record your distribution/release and Bridge version locally. Version requirements can change; installation support is distinct from this project's tested compatibility.
2. Start Bridge yourself, sign in through its own UI, and wait for synchronization. Read the displayed IMAP username, generated password, port, and connection mode. Use a literal loopback address. Do not supply the Proton account password or any SMTP settings.
3. Create a private directory outside the checkout, for example `$HOME/.config/protonmail-mcp`, with mode `0700`. Copy [config.example.json](../examples/config.example.json) there and edit the external copy. Set the JSON, exported public certificate, and separate password file to mode `0600` or `0400` and keep them owned by your user. Use a trusted local editor for the password; keep secrets out of command arguments and shell history. Follow all [configuration requirements](CONFIGURATION.md).
4. Export and verify the public certificate as described below. Set absolute paths for the public certificate and password file in the external JSON. Set `StartTls` for Bridge STARTTLS or `SslOnConnect` for Bridge SSL; use the displayed port rather than assuming its default. Proton documents connection modes in its [settings guide](https://proton.me/support/comprehensive-guide-to-bridge-settings).
5. Start ordinary use with a narrow allowlist such as `INBOX`. For acceptance, use a separate configuration whose sole allowed folder is the dedicated operator-prepared test folder; never use INBOX as the acceptance folder. Configure its exact Bridge full name. No automatic descendants are allowed.
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

## Operator work still required

Prepare the dedicated folder, known UIDVALIDITY/UID identities and full membership/flags manifest using a separate trusted client with read-only inspection. Prepare plain text and HTML-only fixtures with known expected converted text, unread/read examples, an attachment example, and a pre-existing deleted flag if Bridge supports it. Pause other clients/rules and wait for sync. See [the live procedure](TESTING.md#opt-in-real-bridge-acceptance) for exact schema, stages, unsupported-state reporting, and the manual in-flight connection-loss check. This server and its suite never prepare or clean up messages.

Record actual Linux, Bridge, SDK/runtime and package versions, date, stage outcomes, unsupported states, and remaining manual checks in the evidence ledger without sensitive values. Fresh-install success, flag preservation, Bridge restart/connection-loss recovery, and MVP completion remain unverified until that work is performed.
