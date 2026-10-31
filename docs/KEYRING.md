# Linux keyring support

Status: implemented on 2026-10-31 through an asynchronous C# P/Invoke adapter over system libsecret. `PasswordKeyring` is an explicit alternative to legacy `PasswordFile`. Isolated fake-service and separately gated real-GNOME evidence, published-artifact checks and remaining live acceptance are recorded in [PLAN.md](../PLAN.md) and [STATUS.md](../STATUS.md). No personal keyring or Bridge access is part of these tests.

## Native dependencies and APIs

The provider uses libsecret's asynchronous service, collection and item APIs with ordinary GDBus ownership monitoring. It trusts the local session bus and Secret Service. Ownership transitions cancel native work and invalidate affected results before credential acceptance; this continuity check does not authenticate GNOME Keyring or isolate it from a malicious same-user service. The [ownership contract](#ownership-changes-during-lookup) and [context ordering](#implemented-context-and-acceptance-ordering) define the exact behavior.

Tested environment: Zorin OS 18.1, Ubuntu noble base, x86_64; `libsecret-1-0` / `gir1.2-secret-1` **0.21.4-1build3**, GLib/GIO/GObject **2.80.0-6ubuntu3.10**, D-Bus **1.14.10-4ubuntu4.1**, GNOME Keyring **46.1-2ubuntu0.2**, Python D-Bus **1.3.2-5build3**, PyGObject **3.48.2-1**. Runtime sonames are `libsecret-1.so.0`, `libgio-2.0.so.0`, `libgobject-2.0.so.0`, and `libglib-2.0.so.0`. Native development headers/pkg-config files are absent. No installation was attempted. The production ABI is declared in `Credentials/SecretNative.cs`: C calling convention, 32-bit gboolean/gint, native-size GType/gsize and pointer-based GList/GError layouts. No managed binding package is added. The isolated real-GNOME suite observes `dh-ietf1024-sha256-aes128-cbc-pkcs7` negotiation through real libsecret; the fake service rejects that algorithm and exercises libsecret's `plain` fallback. Neither choice establishes confidentiality from the trusted bus or same-user processes. Libsecret/GLib sources are LGPL-2.1-or-later; no native binaries or source were vendored.

Managed versions and both NuGet lock graphs remain unchanged: MCP **2.2.0**, Hosting/PKCS **10.0.13**, MailKit/MimeKit **4.18.1**, Test SDK **18.11.0**, xUnit **2.9.3**, adapter **4.0.1**. The checkout pins SDK **10.0.114**, with tested runtime **10.0.14**. No new package was selected, so no lock-file update is warranted.

Primary API/source review:

- [libsecret 0.21.4 service implementation](https://github.com/GNOME/libsecret/blob/0.21.4/libsecret/secret-service.c).
- [GDBusProxy owner tracking and notification context](https://docs.gtk.org/gio/class.DBusProxy.html), [owner getter](https://docs.gtk.org/gio/method.DBusProxy.get_name_owner.html), and [signal subscription ordering/lifetime](https://docs.gtk.org/gio/method.DBusConnection.signal_subscribe.html).
- [Collection-scoped search flags](https://gnome.pages.gitlab.gnome.org/libsecret/method.Collection.search.html), [single-item secret loading](https://gnome.pages.gitlab.gnome.org/libsecret/method.Item.load_secret.html), [native session negotiation implementation](https://github.com/GNOME/libsecret/blob/0.21.4/libsecret/secret-session.c).

## Scope and security objective

Add an explicit alternative to the protected password file: an operator-provisioned Secret Service item, with GNOME Keyring on Linux as the required supported backend. Keep one account, the same Bridge username/certificate settings, existing read-only mailbox behavior, and exactly the current MCP tools and schemas. No secret-management MCP tools, HTTP, automatic credential discovery, service deployment, or account login are added.

The objective is to avoid an application-owned permanent plaintext password file in keyring mode and avoid retaining that password for the server lifetime. This is not isolation from the same user, root, a compromised desktop bus/keyring, debugger, or launching client. Keyring support does not make Bridge credentials read-only. Storage protection depends on GNOME Keyring configuration, an effective collection password, and OS security; an unlocked collection is available under its service policy. Metadata must contain no password, address, or mailbox data. Secret Service does not mandate application access control and lookup attributes are not secret. See the [Secret Service trust model](https://specifications.freedesktop.org/secret-service/latest-single/).

## Configuration

Keep legacy `PasswordFile` configurations valid and unchanged. Add this alternative fragment to the existing external JSON, removing `PasswordFile` entirely:

```json
{
  "PasswordKeyring": {
    "CollectionAlias": "default",
    "ItemId": "44ec5121-59ce-4c39-8b45-551823feb631"
  }
}
```

This is a fragment, not a complete runnable configuration. `Host`, `Port`, `TlsMode`, `Username`, `CertificateFile`, and folder/download policies retain their existing contracts.

- Exactly one of `PasswordFile` and `PasswordKeyring` must be present. Reject both, neither, explicit nulls, wrong types, duplicate/unknown fields and invalid nested values at startup. Presence, rather than a non-null value, determines mixed-source rejection.
- Both nested fields are required. Initially accept only the case-sensitive collection alias `default`. `ItemId` must be a lowercase canonical UUID in D format, nonzero; it identifies one credential, not an additional account. No defaults, labels, username searches, arbitrary attributes, item object paths, bus addresses or service names are configurable.
- The exact lookup attributes are `application=protonmail-mcp`, `purpose=bridge-imap-password`, and `id=<ItemId>`. Extra service metadata may exist; all three required values must match. Labels are display text only and must never select a credential.
- JSON and public-certificate ownership, private-parent, no-symlink, size and open-handle validation remain mandatory. File-mode password/path checks and attachment-directory overlap checks remain unchanged; keyring mode has no password file to validate. Never create a dummy file or weaken checks for the other paths.
- Configuration, username and certificate changes still require restart. Keyring password changes are observed on the next operation; file mode retains its current startup snapshot/restart semantics. No source switch or cached-secret fallback occurs at runtime.

## Provider and transport design

Introduce one narrow injected asynchronous password-source boundary, owned by the mail adapter rather than MCP tools. Immutable configuration holds the keyring selector, never its retrieved secret. The legacy file source may retain its existing validated startup snapshot for compatibility; distinguish this from the keyring lifetime guarantee. Keep one application and one test project, without a general secret-manager framework.

Use the supported Linux `libsecret-1` asynchronous collection/item APIs through a small audited interop adapter or maintained binding. GNOME documents [libsecret as a Secret Service D-Bus client](https://gnome.pages.gitlab.gnome.org/libsecret/). Use its service/collection API explicitly, not a backend abstraction that can select an alternative file/portal backend. Do not spawn `secret-tool`, a shell, `dbus-send`, or another credential command in the server. No custom cryptography or direct reading of keyring databases.

When changing the adapter, reverify the binding/native ABI, GLib main-context ownership, cancellation, native resource release, service-owner behavior and default session negotiation against the selected versions. Pin any managed dependency exactly, review advisories/licenses and lock-file changes, and document the native package/version requirements. Do not invent package APIs or claim a package choice has already been validated. Verify ownership monitoring and cancellation against the acceptance criteria. Any demonstrated inability to meet the contract must be reported; it does not permit silent fallback or a synchronous call on a background task.

Only the user's existing local Unix session bus and `org.freedesktop.secrets` service are allowed. Treat the inherited session-bus address as desktop transport context, not a password/configuration override. Reject remote/TCP transports and automatic spawning of a new bus. Never set or copy a personal bus address into tracked launch files. Service activation on that existing bus is permissible within the operation deadline. Do not launch a keyring daemon or supply a collection master password. Use the standard service on that bus, with the ownership monitoring and result-invalidation rules below. Owner loss/replacement during lookup fails the mailbox operation without authentication or an automatic retry; a later caller operation may resolve the service afresh.

Use libsecret's session negotiation, without implementing a new encryption algorithm or claiming bus transport is an application-isolation boundary. Audit and record whether the tested library negotiates encrypted or plain secret transfer. No confidentiality guarantee against the trusted session bus or same-user processes is made. If a deployment needs that guarantee, this desktop design is insufficient; do not market encryption at rest as solving it.

## Ownership changes during lookup

Use ordinary libsecret service/collection/item APIs with a narrow operation-scoped monitor. The planned application rule is:

> If service ownership changes during a credential lookup, cancel the native work, discard every result from that lookup, and fail the mailbox operation before authentication. Do not retry within that operation. A later explicit caller operation starts fresh.

Monitor from owner resolution before alias lookup through the point where a validated credential is accepted for authentication. Establish monitoring without a gap between reading the owner and subscribing to changes. Latch any loss or transition for the entire lookup, including loss followed by reacquisition by the same process or a change away and back; comparing only the starting and ending names is insufficient. Owner identity tracks continuity, not provider authenticity.

Coordinate ownership notifications, native completion callbacks and credential acceptance on a documented GLib main-context/lifetime model. On invalidation, cancel the actual native operation and drain its callbacks; late successful callbacks must only dispose their results. Check the latched state and cancellation/deadline before credential handoff. Map owner loss/replacement to `credential_unavailable`, retaining caller-cancellation and timeout precedence. Native cleanup and mailbox-gate release share the existing deadline. No automatic new lookup, cached-password use or source fallback is allowed.

The implementation must define and test the credential-acceptance boundary. Changes after a successfully completed credential handoff do not revoke the acquired password or an authenticated IMAP connection; subsequent mailbox calls retrieve afresh. This is not an atomic snapshot of keyring state. The contract does not guarantee that no internal metadata request can reach a replacement while cancellation is being dispatched. It requires rejection of lookup results affected by an ownership transition before acceptance, with no authentication from those results.

Validate this with an isolated service that changes ownership before alias resolution, between search and load, during a delayed load, and as a successful callback races cancellation/credential handoff. Include owner loss, replacement, release/reacquisition and change-away-and-back; require no IMAP authentication, prompt disposal, drained callbacks, released gate, and recovery on a new caller operation. Document event ordering and remaining guarantees precisely; the native provider and real-adapter process tests cover these transitions.

## Implemented context and acceptance ordering

Each lookup starts a dedicated thread with a new thread-default GLib main context; it pumps asynchronous native operations rather than running synchronous credential I/O on a background task. It obtains GIO's session-bus connection, subscribes to the bus daemon's `NameOwnerChanged` for exactly `org.freedesktop.secrets`, then constructs a fresh libsecret service. AddMatch and subsequent owner resolution use the same connection. Initial service activation is permitted; every loss or replacement latches invalidation, including transitions during construction. A final `NameHasOwner` round trip and context drain order prior bus ownership notifications before the acceptance check. Completion callbacks always run their finish method; invalidated results are disposed. Caller cancellation reaches GCancellable and takes precedence over owner failure.

The credential-acceptance boundary is the final token/latch check on that context, immediately before returning the validated lease into cleanup. Events arriving afterward do not revoke that accepted lease, even while native cleanup or IMAP authentication is pending. The mailbox checks its token again before supplying credentials. This boundary cannot promise an instantaneous snapshot of changes not yet observed by the bus/context. Tests send loss/replacement/reacquisition/away-and-back before alias resolution completes, after search, during delayed load and after a successful secret reply; they require no LOGIN and fresh-call recovery. A held LOGIN response verifies the post-handoff non-revocation rule.

Before the lookup task completes, unsubscribe and drain subscription destroy-notify, release secret values, items/lists/hash tables/collection/service, asynchronously close the executable's GIO bus connection, drain completion, and release the context. Connection closure also ends remote Secret Service sessions. This application has no other GIO consumers and serializes all mailbox calls; adding independent GIO consumers would require revisiting this connection ownership. GCancellable registration is disposed before its native reference is released. The mailbox gate remains held throughout cleanup. Native libraries are loaded only on use; keyring lookup rejects inherited `SECRET_SERVICE_BUS_NAME`, `G_DBUS_DEBUG` and `G_MESSAGES_DEBUG` before loading to prevent service redirection and native wire/metadata logging; file mode and MCP discovery need no libsecret or session bus.

Application decoding copies at most 4,096 bytes, accepts strict UTF-8 `text/plain` (optionally `charset=utf-8`/`UTF-8`), and clears its owned byte buffer. Object-path copies are capped at 4,096 UTF-8 bytes and content-type copies at 128; required attributes are compared natively. Classification examines at most 4,096 list nodes and rejects more than one distinct object path. Libsecret/GDBus allocate replies, item proxies, attributes and the complete search list before these checks; cleanup must still release every returned reference. These are not wire/preallocation or constant-memory limits. Managed strings, MailKit copies and native internals cannot be reliably zeroized.

## Lookup, lifetime and failure behavior

1. Validate the MCP request/folder policy and acquire the existing mailbox gate under its existing 30-second deadline. No keyring connection, lookup or unlock during configuration loading, MCP initialization or tool discovery. File-mode startup validation stays unchanged; missing native keyring dependencies must not break file mode.
2. Establish required Bridge TLS and verify the exact certificate pin. Only then retrieve the keyring password, before authentication. A denied request, queued cancellation, wrong pin or failed TLS must not query the keyring.
3. With ownership monitoring active, resolve only `default`; never enumerate collections or search across all collections. Missing alias fails. If the collection is locked, fail without unlocking. Search that collection with all three attributes, requesting **all matches**, including locked items. Require exactly one distinct item; zero fails as missing, multiple fail as ambiguous, even if only one is unlocked. Never choose the first/latest item. Libsecret's [collection search API](https://gnome.pages.gitlab.gnome.org/libsecret/method.Collection.search.html) defaults to first-match behavior unless `SECRET_SEARCH_ALL` is set; do not enable `SECRET_SEARCH_UNLOCK` or `SECRET_SEARCH_LOAD_SECRETS` during search.
4. Recheck the selected item's attributes and lock state, then load only its secret. Never call Unlock, Prompt, Create, Store, Delete, SetAlias, or property setters. A deletion or lock during lookup fails closed; owner transitions invalidate the lookup as specified above. The API is not transactional: a concurrent legitimate update may supply either version; no snapshot or resistance to a malicious same-user service is promised.
5. Validate a nonempty secret of at most 4,096 bytes before managed decoding. Accept strict UTF-8 text, preserve spaces, reject blank values, NUL and controls. Unlike the file format, keyring values have no terminal-newline convenience: reject CR/LF rather than trimming. Validate reported native length before copying; reject malformed/missing values, unsupported encodings and invalid replies. Bound application-side search/result processing; explicitly document any allocations already performed by libsecret/D-Bus rather than claiming a preallocation wire cap.
6. After the ownership/cancellation acceptance check, authenticate once using the existing single-attempt guard; no second lookup, alternative item, mechanism retry, password-file fallback or cached credential retry on failure. Release secret references promptly after authentication, including failure/cancellation. Use disposable ownership for native secret values, clear owned mutable buffers in `finally`, and avoid unnecessary copies or secret-bearing `ToString`. Managed strings and MailKit/native internal copies cannot be reliably erased; do not promise zeroization or use `SecureString` as a Linux protection claim.
7. Continue the existing read-only operation and cleanup. Retrieval, native cancellation completion, session disposal and mailbox cleanup share the remaining deadline. Cancellation must reach the actual native operation, not merely stop awaiting it. Drain callbacks before releasing their state, close owned sessions/connections, release the gate, and prove a subsequent call works. No lingering task, native callback, subprocess or cached session may retain the password. Deadline checks precede authentication and result publication.

Locking or rotating the item after authentication does not revoke that already authenticated IMAP connection; subsequent calls retrieve afresh. Do not store the value in singleton settings, tool arguments/results, environment variables, temp files or diagnostics. This lifetime rule is specific to keyring mode; improving the legacy snapshot is separate scope.

Use fixed sanitized configuration errors for source/selector errors. Runtime errors in the existing error envelope are `credential_unavailable` (bus/native service absent, permission denied or owner loss/replacement during lookup), `credential_locked`, `credential_not_found`, `credential_ambiguous`, and `credential_invalid` (bad secret/reply). Preserve existing authentication/TLS, caller-cancellation and timeout outcomes, with cancellation/deadline taking precedence. GIO/D-Bus request timeouts use `timeout`, including a native timeout earlier than the overall deadline. Never include attributes, item paths, labels, bus addresses, username, secret bytes or native exception text in stdout/stderr/errors. No UI prompt or stdin read may compete with MCP stdio.

## GNOME Keyring provisioning and migration

Start with the [step-by-step GNOME Keyring setup](LINUX-SETUP.md#gnome-keyring-setup) for package prerequisites, preparing the default collection, running this recipe, configuring the selector and restarting the MCP host. This section contains the provisioning script used by that walkthrough.

The operator installs GNOME Keyring and libsecret using their distribution packages and launches the server in the same desktop session as the intended Secret Service. Merely installing packages does not establish that GNOME Keyring owns the service; another provider can own that bus name. GNOME Keyring is the required tested backend, not an assumption about every Linux desktop. Headless, SSH, container and system-service contexts need an available, deliberately prepared session and unlocked collection; unattended unlock, systemd credentials and other providers are deferred.

The operator prepares the persistent default collection and unlocks it through the trusted desktop before calling tools. Provision a **new dedicated MCP item** with the exact attributes above, using the Bridge-generated IMAP password displayed by Bridge. Never discover, reuse, rename, delete or modify Bridge's internal keyring entries. Never use the Proton account password or the collection master password as the IMAP secret.

Save this operator-only example in a private file outside the checkout, then run it with `/usr/bin/python3 /absolute/private/provision.py` from an interactive terminal in the intended desktop session. It requires PyGObject and the Secret 1 typelib in addition to libsecret. The example is API-verified with libsecret 0.21.4 / PyGObject 3.48.2 and exercised with a synthetic password on the isolated GNOME fixture. It creates a new item, prints only its random selector UUID, and accepts the password through hidden terminal input. Do not run it as part of server startup. The server never imports or executes it.

<!-- provisioning-example:start -->
```python
import getpass
import sys
import uuid
import gi

gi.require_version("Secret", "1")
from gi.repository import Secret


def provision(item_id, password):
    service = Secret.Service.get_sync(Secret.ServiceFlags.NONE, None)
    collection = Secret.Collection.for_alias_sync(
        service, "default", Secret.CollectionFlags.NONE, None)
    if collection is None or collection.get_locked():
        raise RuntimeError("prepare_unlocked_default_collection")
    attributes = {"application": "protonmail-mcp",
                  "purpose": "bridge-imap-password", "id": item_id}
    if collection.search_sync(None, attributes, Secret.SearchFlags.ALL, None):
        raise RuntimeError("selector_already_exists")
    value = Secret.Value.new(password, -1, "text/plain; charset=utf-8")
    return Secret.Item.create_sync(collection, None, attributes,
        "Proton Mail MCP credential", value, Secret.ItemCreateFlags.NONE, None)


if __name__ == "__main__":
    if not sys.stdin.isatty() or not sys.stderr.isatty():
        raise SystemExit("interactive_terminal_required")
    item_id = str(uuid.uuid4())
    password = getpass.getpass("Bridge IMAP password: ")
    try:
        if not password.strip() or len(password.encode("utf-8")) > 4096 or any(
                ord(c) < 32 or 127 <= ord(c) <= 159 for c in password):
            raise ValueError("invalid_password")
        provision(item_id, password)
        print(item_id)
    except Exception:
        raise SystemExit("provisioning_failed") from None
    finally:
        password = None
```
<!-- provisioning-example:end -->

Use the printed UUID as `PasswordKeyring.ItemId`; keep `CollectionAlias` exactly `default`. The label has no selection authority. The recipe does not inspect Bridge entries. Existing items require deliberate operator maintenance through a trusted keyring tool; do not rerun provisioning with an existing selector or put secret values in attributes. Python/native copies share the ordinary same-user memory limits.

Migration: provision the dedicated item, replace `PasswordFile` with `PasswordKeyring` in the private external JSON, restart, and separately authorize any live mailbox verification. After successful verification, the operator may retire the old plaintext file and its editor/backups according to their retention policy; deletion is not assured secure erasure. The server never deletes it. Rollback is an explicit configuration edit and restart with an operator-managed protected file, never an automatic fallback.

## Acceptance matrix

The matrix distinguishes native fake-service evidence, isolated real-GNOME compatibility and separately authorized live work. Exact final commands/counts and limitations are recorded in PLAN/STATUS. Passing synthetic tests do not satisfy the live row.

| Area | Required evidence | Current outcome |
| --- | --- | --- |
| Backward compatibility | Existing file configurations, permission/open-handle checks, startup errors, no keyring dependency/access in file mode; ambiguous/null/unknown/duplicate source fields rejected; unchanged tools, annotations, schemas and read-only traffic. | Passed focused source validation and file-mode regressions, including a working file-backed operation with no bus and an incomplete libsecret dependency. Three/four-tool discovery is unchanged. Definitive complete suite results are in STATUS. |
| Selection and permissions | Private synthetic Secret Service verifies collection-scoped exact attributes; missing alias/item, locked collection/item, duplicate locked/unlocked matches, extra metadata, stale/deleted item, owner replacement, denied access and no cross-collection fallback. Assert no unlock/prompt or mutation calls. | Passed real libsecret + private fake-service selection/permission cases through the actual MCP/MailKit adapter, including locked/unlocked ambiguity, extra metadata, deletion, attribute mismatch, denied access and owner transitions. Fixture method observation rejects unlock/prompt/mutation. |
| Input and leak resistance | Empty/blank/control/newline/NUL/invalid UTF-8, exact 4,096-byte limit/overflow, missing or malformed native replies; synthetic canary secrets absent from errors, logs, stdout, process arguments and files. Search and native allocation limits documented. | Passed unit/native/process rejection and exact-boundary cases, including a deliberately malformed D-Bus signature, strict UTF-8, spaces and 4,096/4,097-byte values. Owned buffers are cleared; canaries are excluded from MCP protocol/errors/argv/environment and fixture plaintext files. Native allocation limits and managed-copy limits are explicit above. |
| Authentication and lifetime | Real MailKit adapter plus synthetic TLS/IMAP and credential service prove pin rejection before lookup, no authentication from ownership-invalidated results (including late successful callbacks), one retrieval/authentication attempt, fresh value on next operation, no retained-keyring-password fallback, failed authentication, buffer/resource disposal and recovery. | Passed actual native-provider/MailKit tests for pin-before-lookup, rotation/fresh retrieval, rejection without a second attempt, 16 ownership-change cases with no LOGIN, delayed reply disposal, fresh-call recovery and post-handoff non-revocation. Tracking leases verify prompt disposal on success, rejection and authentication cancellation; fixture checks the GIO client disconnects. |
| Cancellation and cleanup | Queue, bus connection/search/load, native callback completion, authentication, timeout, owner loss/replacement/reacquisition, change-away-and-back, completion/handoff races, stdin EOF and process shutdown; no leftover operations/owned processes or leaked gate. Standard tests use only explicitly isolated buses and synthetic secrets. | Passed native bus-handshake/alias/search/load and queue cancellation, authentication cancellation, shared 30-second deadline, late service replies, ownership/completion/handoff ordering, stdin EOF and fresh calls. Fixtures reap owned children/remove storage. No hard real-time or reliable erasure of library/managed internal copies is claimed. |
| GNOME compatibility | Separate opt-in test category `KeyringIntegration`: real libsecret with real GNOME Keyring on a private test bus and temporary private HOME/XDG directories, synthetic collection/item/password only, no Bridge. Cover unlocked success, locked failure without UI, missing/duplicate items, rotation/recovery, service restart during lookup with no authentication, and cleanup. Record distro, architecture, GLib/libsecret, binding, D-Bus and GNOME Keyring versions. | Four gated isolated GNOME provider/recipe tests cover this row; current verification is recorded in STATUS. Real libsecret negotiated encrypted transfer. Locked failure, duplicate/missing items, rotation, owner-loss/restart recovery and cleanup passed for the exact native versions above. Without the gate all four are skipped, not accepted. |
| Published artifact | MCP discovery works without a bus/available keyring; native dependency failure is sanitized on use; successful synthetic mailbox call using isolated keyring and wrong-pin/no-lookup case; protocol-only stdout and child cleanup against the published DLL. | Implementation verification before documentation/test cleanup passed 266 published Protocol/MCP tests, 0 failed/skipped. Native process cases verify absent-bus discovery, unavailable native API, debug-override rejection, successful lookup, wrong pin, stdout hygiene and cleanup. The corresponding standard suite passed 482 tests, 0 failed/skipped. Current cleanup checks, historical commands and matching DLL identity are in STATUS. |
| Live Bridge | Separately authorized external configuration and prepared fixture folder prove keyring-backed authentication and unchanged flags/membership. Isolated GNOME tests do not establish live Bridge acceptance or personal desktop-client access. | Not authorized; not run. |

Keep standard offline `Unit`, `Protocol` and `Mcp` tests independent of installed GNOME Keyring and a real desktop. Native-adapter tests may require libsecret and a fixture-owned private bus; document these nonsecret prerequisites. Add an explicit opt-in gate for `KeyringIntegration`, default off even when the category is accidentally selected. Update the category-enforcement test, developer commands, workspace tasks and automation together: the current standard filter is `Category!=LiveBridge&Category!=KeyringIntegration`. Run integration tests separately and report an absent prerequisite/gate as unverified, never a compatibility pass.

The integration harness must create its own bus and synthetic storage, override inherited bus/HOME/XDG state for children, and fail closed if isolation cannot be established. Never connect to the developer's existing session to seed fixtures. Track and reap only owned children; bounded shutdown must kill/wait when required. No cleanup of personal keyrings, other processes or mailbox data. Test-only bus injection must not become a production MCP/configuration override.
