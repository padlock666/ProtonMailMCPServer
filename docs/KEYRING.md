# GNOME Keyring credential contract

GNOME Keyring is the sole credential source. [Linux setup](LINUX-SETUP.md#gnome-keyring-setup) gives package, desktop and `secret-tool store` instructions. [Configuration](CONFIGURATION.md#gnome-keyring) defines the required `Keyring.Application` and `Keyring.Account` selectors.

## Lookup behavior

The implementation adopts the application/account lookup in the GnomeKeyringTest demo. It uses libsecret's asynchronous `secret_service_lookup` with a null schema and exactly the `application` and `account` attributes. This is the lookup underlying the demo's password API; a fresh owned service avoids retaining a cached backend or cancelled prompt between operations. It searches across the user's collections, may unlock a matching item through a desktop prompt, and lets libsecret choose a retrievable match. Operators must keep the pair unique. The label has no lookup authority. This behavior, including prompts and selection across collections, was explicitly approved on 2026-11-01.

The application does not provision, update or delete keyring items, discover Bridge entries, start a bus or daemon, read terminal input, or print the password. Only the operator runs `secret-tool store`. An unlock can expose other items in that collection to the same desktop session.

## Lifetime and validation

Lookup occurs once per mailbox operation, after pinned TLS and before a single authentication attempt. Initialization/discovery, denied requests, queued cancellation and failed pins never retrieve a credential. A later operation retrieves afresh, including after rotation or authentication failure. There is no credential retry or persistent application password cache.

Each lookup owns a thread-default GLib main context and pumps asynchronous native I/O. Caller cancellation and the shared 30-second mailbox deadline reach GCancellable, including a pending desktop prompt. Native cancellation is dispatched onto the lookup context because libsecret starts an asynchronous Dismiss call during prompt cancellation; its completion must run on the same pumped context. Every callback runs its finish method before cleanup. The application uses `secret_service_lookup_finish` to retain the native length, then validates text content type, 1–4,096 bytes, strict UTF-8 and absence of blank/NUL/control values. This prevents text conversion from silently truncating an embedded NUL. Spaces are preserved; CR/LF are invalid.

The SecretValue is released promptly, owned byte buffers are cleared, the owned service proxy is released and the executable's GIO connection is closed before returning a disposable password lease. MailKit requires a managed string; the lease drops its reference immediately after authentication, including failure/cancellation. Managed strings and library copies cannot be reliably erased. Libsecret/GDBus allocate native replies before application bounds apply; these are not wire/preallocation limits.

## Ownership changes during lookup

Monitoring subscribes to `org.freedesktop.secrets` ownership before lookup. Loss/replacement latches invalidation, cancels native work and rejects results without authentication or retry. A same-connection bus round trip and context drain order observed notifications before credential acceptance. Cancellation takes precedence. Events after the final acceptance check do not revoke the accepted credential or an authenticated IMAP connection; unobserved concurrent events cannot be made instantaneous.

Callbacks and subscription destroy-notify drain before their state is released. All mailbox calls serialize, and this executable has no other GIO consumers; independent GIO consumers would require revisiting connection ownership. Failure and cancellation release the mailbox gate so an explicit fresh call can recover.

## Native dependencies and APIs

Linux requires system libsecret, GLib, GIO and GObject matching the process architecture, a local Unix session bus and the intended GNOME Keyring desktop session. Dependencies load lazily. No managed package was added and exact NuGet pins/locks are unchanged. The provider rejects TCP, autolaunch and multiple bus transports, plus inherited `SECRET_SERVICE_BUS_NAME`, `G_DBUS_DEBUG` and `G_MESSAGES_DEBUG` overrides before native loading.

The local OS/user, session bus, Secret Service and launching client are trusted. Service ownership monitoring does not authenticate the provider. An unlocked keyring does not isolate credentials from same-user applications or root. Native secure-memory behavior does not guarantee protection from debugging, swap or crash dumps.

API references: [GLib context dispatch](https://docs.gtk.org/glib/method.MainContext.invoke_full.html), [libsecret prompt cancellation](https://github.com/GNOME/libsecret/blob/0.21.4/libsecret/secret-prompt.c), [asynchronous attribute lookup](https://gnome.pages.gitlab.gnome.org/libsecret/method.Service.lookup.html), [password lookup implementation](https://github.com/GNOME/libsecret/blob/0.21.4/libsecret/secret-password.c), and [libsecret lookup/unlock selection](https://github.com/GNOME/libsecret/blob/0.21.4/libsecret/secret-methods.c).

## Failures and verification

Errors contain fixed codes only: `credential_unavailable`, `credential_not_found`, `credential_invalid`, native `credential_locked` where reported, and the existing authentication/TLS/cancellation/timeout outcomes. A dismissed unlock prompt can return `credential_not_found`. Attributes, paths, labels, native exception text and secret bytes never enter diagnostics or MCP results.

Standard tests use injected synthetic credentials or private fake-service buses and synthetic TLS/IMAP; they never use personal keyrings. Fake-service tests cover prompts, dismissal/cancellation, validation, rotation, owner changes and cleanup through the actual adapter. The separately gated GNOME suite provisions with `secret-tool store` on isolated temporary storage and checks real libsecret/GNOME retrieval. See [testing](TESTING.md#keyring-verification) and [PLAN](../PLAN.md) for commands and actual results. Isolated tests do not establish live Bridge compatibility or personal desktop prompt behavior.
