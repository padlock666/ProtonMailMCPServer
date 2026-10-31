using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using ProtonMailMcp.Configuration;
using ProtonMailMcp.Contracts;
using static ProtonMailMcp.Credentials.SecretNative;

namespace ProtonMailMcp.Credentials;

// Each lookup owns a thread-default GLib context. Only asynchronous native I/O is used;
// this thread pumps callbacks, never a synchronous credential API on Task.Run.
internal sealed class LibsecretPasswordSource(KeyringSelector selector) : IPasswordSource
{
    public Task<PasswordLease> GetAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<PasswordLease>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                ValidateTransport(Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS"));
                // Native overrides/debug output must not redirect the service or dump bus
                // payloads/metadata into MCP stdout or diagnostics.
                foreach (var variable in new[] { "SECRET_SERVICE_BUS_NAME", "G_DBUS_DEBUG", "G_MESSAGES_DEBUG" })
                    if (Environment.GetEnvironmentVariable(variable) is not null)
                        throw new MailboxException(MailboxError.CredentialUnavailable);
                completion.SetResult(new Lookup(selector, cancellationToken).Run());
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            { completion.SetCanceled(cancellationToken); }
            catch (MailboxException error) { completion.SetException(error); }
            catch (Exception) { completion.SetException(new MailboxException(MailboxError.CredentialUnavailable)); }
        }) { IsBackground = true, Name = "credential-lookup" };
        thread.Start();
        return completion.Task;
    }

    internal static void ValidateTransport(string? address)
    {
        // Require one explicit local Unix transport; disallow autolaunch, alternatives and TCP.
        if (!OperatingSystem.IsLinux() || string.IsNullOrEmpty(address) || address.Contains(';') ||
            address.Any(char.IsControl) || !address.StartsWith("unix:", StringComparison.Ordinal))
            throw new MailboxException(MailboxError.CredentialUnavailable);
        var fields = address[5..].Split(',');
        var endpoint = false;
        foreach (var field in fields)
        {
            var pair = field.Split('=', 2);
            if (pair.Length != 2 || pair[1].Length == 0 || pair[0] is not ("path" or "abstract" or "guid") ||
                (pair[0] != "guid" && endpoint))
                throw new MailboxException(MailboxError.CredentialUnavailable);
            if (pair[0] != "guid") endpoint = true;
        }
        if (!endpoint) throw new MailboxException(MailboxError.CredentialUnavailable);
    }

    internal static string Decode(byte[] bytes)
    {
        try
        {
            if (bytes.Length is < 1 or > 4096) throw new DecoderFallbackException();
            var text = new UTF8Encoding(false, true).GetString(bytes);
            if (string.IsNullOrWhiteSpace(text) || text.Any(char.IsControl)) throw new DecoderFallbackException();
            return text;
        }
        catch (DecoderFallbackException) { throw new MailboxException(MailboxError.CredentialInvalid); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    private sealed class Lookup(KeyringSelector selector, CancellationToken token)
    {
        private nint context, cancellable, connection;
        private bool changed, seenOwner, destroyed;
        private readonly List<Ready> callbacks = [];
        private Signal? ownerCallback;
        private Destroy? subscriptionDestroyed;

        private void Check()
        {
            token.ThrowIfCancellationRequested();
            if (changed) throw new MailboxException(MailboxError.CredentialUnavailable);
        }

        // Always finish/drain the actual native request, even on cancellation and late success.
        private nint Await(Action<Ready> start, Func<nint, (nint Value, nint Error)> finish,
            Action<nint>? release = null, bool check = true)
        {
            nint value = 0, error = 0;
            var done = false;
            var callbackFailed = false;
            Ready callback = (_, result, _) =>
            {
                // Never let a managed binding/loading exception cross a reverse P/Invoke.
                try { (value, error) = finish(result); }
                catch (Exception) { callbackFailed = true; }
                finally { done = true; }
            };
            callbacks.Add(callback); // rooted until all callbacks and native objects have drained
            start(callback);
            while (!done) g_main_context_iteration(context, 1);
            try
            {
                while (g_main_context_pending(context) != 0) g_main_context_iteration(context, 0);
                if (check) Check();
                if (callbackFailed) throw new MailboxException(MailboxError.CredentialUnavailable);
                if (error != 0) throw Error(error);
                return value;
            }
            catch { if (value != 0) release?.Invoke(value); throw; }
            finally { if (error != 0) g_error_free(error); }
        }

        private static MailboxException Error(nint error)
        {
            var native = Marshal.PtrToStructure<NativeError>(error);
            if (native.Domain == g_io_error_quark() && native.Code == 24)
                return new MailboxException(MailboxError.Timeout);
            if (native.Domain == g_io_error_quark() && native.Code is 13 or 35)
                return new MailboxException(MailboxError.CredentialInvalid);
            if (native.Domain == g_dbus_error_quark())
                return new MailboxException(native.Code switch
                {
                    4 or 12 or 20 => MailboxError.Timeout,
                    16 or 36 => MailboxError.CredentialInvalid,
                    41 => MailboxError.CredentialNotFound,
                    _ => MailboxError.CredentialUnavailable
                });
            var code = native.Domain == secret_error_get_quark() ? native.Code : 0;
            return new MailboxException(code switch
            {
                2 => MailboxError.CredentialLocked,
                3 => MailboxError.CredentialNotFound,
                1 or 5 or 6 or 7 or 8 or 9 => MailboxError.CredentialInvalid,
                _ => MailboxError.CredentialUnavailable
            });
        }

        private nint BusCall(string method, nint parameters) => Await(
            cb => g_dbus_connection_call(connection, "org.freedesktop.DBus", "/org/freedesktop/DBus",
                "org.freedesktop.DBus", method, parameters, 0, 0, -1, cancellable, cb, 0),
            r => { var v = g_dbus_connection_call_finish(connection, r, out var e); return (v, e); }, g_variant_unref);

        private void Barrier()
        {
            // Same connection: bus orders NameOwnerChanged before this reply. The context is
            // drained by Await before acceptance, including loss/reacquisition and away/back.
            var parameter = g_variant_new_string("org.freedesktop.secrets");
            var tuple = g_variant_new_tuple([parameter], 1);
            var reply = BusCall("NameHasOwner", tuple);
            try
            {
                var child = g_variant_get_child_value(reply, 0);
                try
                {
                    if (g_variant_get_boolean(child) == 0)
                        throw new MailboxException(MailboxError.CredentialUnavailable);
                    seenOwner = true;
                }
                finally { g_variant_unref(child); }
            }
            finally { g_variant_unref(reply); }
        }

        internal PasswordLease Run()
        {
            context = g_main_context_new();
            g_main_context_push_thread_default(context);
            nint service = 0, collection = 0, matches = 0, attributes = 0, value = 0;
            uint subscription = 0;
            PasswordLease? lease = null;
            try
            {
                cancellable = g_cancellable_new();
                using var registration = token.Register(() => g_cancellable_cancel(cancellable));
                try
                {
                    connection = Await(cb => g_bus_get(2, cancellable, cb, 0),
                        r => { var v = g_bus_get_finish(r, out var e); return (v, e); }, g_object_unref);
                    // Never terminate the MCP host if the bus disappears.
                    g_dbus_connection_set_exit_on_close(connection, 0);
                    subscriptionDestroyed = _ => destroyed = true;
                    ownerCallback = (_, _, _, _, _, parameters, _) =>
                    {
                        var old = g_variant_get_child_value(parameters, 1);
                        try
                        {
                            var oldOwner = g_variant_get_string(old, out var length);
                            if (length != 0 || seenOwner)
                            {
                                changed = true; // latch, never cleared by later acquisition
                                g_cancellable_cancel(cancellable);
                            }
                            else if (oldOwner != 0) seenOwner = true; // first service activation
                        }
                        finally { g_variant_unref(old); }
                    };
                    subscription = g_dbus_connection_signal_subscribe(connection, "org.freedesktop.DBus",
                        "org.freedesktop.DBus", "NameOwnerChanged", "/org/freedesktop/DBus",
                        "org.freedesktop.secrets", 0, ownerCallback, 0, subscriptionDestroyed);
                    // Subscription is installed before constructing/resolving the service. The
                    // first acquisition is permitted; any loss/change, including during init, fails.
                    service = Await(cb => secret_service_open(secret_service_get_type(), null, 0, cancellable, cb, 0),
                        r => { var v = secret_service_open_finish(r, out var e); return (v, e); }, g_object_unref);
                    if (service == 0 || g_dbus_proxy_get_connection(service) != connection)
                        throw new MailboxException(MailboxError.CredentialUnavailable);
                    Barrier();
                    collection = Await(cb => secret_collection_for_alias(service, selector.CollectionAlias, 0, cancellable, cb, 0),
                        r => { var v = secret_collection_for_alias_finish(r, out var e); return (v, e); }, g_object_unref);
                    if (collection == 0) throw new MailboxException(MailboxError.CredentialNotFound);
                    if (secret_collection_get_locked(collection) != 0) throw new MailboxException(MailboxError.CredentialLocked);
                    attributes = NewAttributes(selector.ItemId);
                    matches = Await(cb => secret_collection_search(collection, 0, attributes, 2, cancellable, cb, 0),
                        r => { var v = secret_collection_search_finish(collection, r, out var e); return (v, e); }, FreeMatches);
                    if (matches == 0) throw new MailboxException(MailboxError.CredentialNotFound);
                    // libsecret has already allocated the list. Bound application processing;
                    // don't choose an unlocked match from an ambiguous locked/unlocked pair.
                    nint item = 0;
                    var paths = new HashSet<string>(StringComparer.Ordinal);
                    var count = 0;
                    for (var node = matches; node != 0; node = Marshal.ReadIntPtr(node, IntPtr.Size))
                    {
                        if (++count > 4096) throw new MailboxException(MailboxError.CredentialAmbiguous);
                        var candidate = Marshal.ReadIntPtr(node);
                        var path = BoundedText(g_dbus_proxy_get_object_path(candidate), 4096);
                        if (path is null) throw new MailboxException(MailboxError.CredentialInvalid);
                        paths.Add(path);
                        if (paths.Count > 1) throw new MailboxException(MailboxError.CredentialAmbiguous);
                        item = candidate;
                    }
                    Check();
                    var actual = secret_item_get_attributes(item);
                    try
                    {
                        if (actual == 0) throw new MailboxException(MailboxError.CredentialInvalid);
                        foreach (var pair in RequiredAttributes(selector.ItemId))
                            if (g_strcmp0(g_hash_table_lookup(actual, pair.Key), pair.Value) != 0)
                                throw new MailboxException(MailboxError.CredentialInvalid);
                    }
                    finally { if (actual != 0) g_hash_table_unref(actual); }
                    if (secret_item_get_locked(item) != 0) throw new MailboxException(MailboxError.CredentialLocked);
                    var loaded = Await(cb => secret_item_load_secret(item, cancellable, cb, 0),
                        r => { var ok = secret_item_load_secret_finish(item, r, out var e); return (ok, e); });
                    if (loaded == 0) throw new MailboxException(MailboxError.CredentialInvalid);
                    value = secret_item_get_secret(item);
                    if (value == 0) throw new MailboxException(MailboxError.CredentialInvalid);
                    var contentType = BoundedText(secret_value_get_content_type(value), 128);
                    if (contentType is not ("text/plain" or "text/plain; charset=utf-8" or "text/plain; charset=UTF-8"))
                        throw new MailboxException(MailboxError.CredentialInvalid);
                    var bytes = secret_value_get(value, out var size);
                    if (bytes == 0 || size is < 1 or > 4096) throw new MailboxException(MailboxError.CredentialInvalid);
                    var buffer = new byte[(int)size];
                    try { Marshal.Copy(bytes, buffer, 0, buffer.Length); lease = new PasswordLease(Decode(buffer)); }
                    finally { CryptographicOperations.ZeroMemory(buffer); }
                    Barrier();
                    Check();
                    // Credential acceptance boundary: on this context after the bus barrier and
                    // queued notifications. Subsequent owner changes do not revoke this lease.
                    return lease;
                }
                catch { lease?.Dispose(); throw; }
                finally
                {
                    // Finish methods above ran even after cancellation. Unsubscribe on the same
                    // thread, wait for destroy-notify, then destroy proxies before closing the bus.
                    if (subscription != 0)
                    {
                        g_dbus_connection_signal_unsubscribe(connection, subscription);
                        while (!destroyed) g_main_context_iteration(context, 1);
                    }
                    if (value != 0) secret_value_unref(value);
                    if (matches != 0) FreeMatches(matches);
                    if (attributes != 0) g_hash_table_unref(attributes);
                    if (collection != 0) g_object_unref(collection);
                    if (service != 0) g_object_unref(service);
                    if (connection != 0)
                    {
                        // This executable has no other GIO consumers; mailbox operations serialize.
                        // Closing its bus connection also closes remote Secret Service sessions.
                        Await(cb => g_dbus_connection_close(connection, 0, cb, 0),
                            r => { g_dbus_connection_close_finish(connection, r, out var e); if (e != 0) g_error_free(e); return (0, 0); },
                            check: false);
                        g_object_unref(connection);
                    }
                }
            }
            catch { lease?.Dispose(); throw; }
            finally
            {
                if (cancellable != 0) g_object_unref(cancellable);
                while (g_main_context_pending(context) != 0) g_main_context_iteration(context, 0);
                g_main_context_pop_thread_default(context);
                g_main_context_unref(context);
                GC.KeepAlive(callbacks);
                GC.KeepAlive(ownerCallback);
                GC.KeepAlive(subscriptionDestroyed);
            }
        }

        private static string? BoundedText(nint pointer, int limit)
        {
            if (pointer == 0) return null;
            for (var length = 0; length <= limit; length++)
                if (Marshal.ReadByte(pointer, length) == 0) return Marshal.PtrToStringUTF8(pointer, length);
            return null;
        }

        private static KeyValuePair<string, string>[] RequiredAttributes(string id) =>
            [new("application", "protonmail-mcp"), new("purpose", "bridge-imap-password"), new("id", id)];

        private static nint NewAttributes(string id)
        {
            var library = NativeLibrary.Load(GLib);
            try
            {
                var table = g_hash_table_new_full(NativeLibrary.GetExport(library, "g_str_hash"),
                    NativeLibrary.GetExport(library, "g_str_equal"), NativeLibrary.GetExport(library, "g_free"),
                    NativeLibrary.GetExport(library, "g_free"));
                foreach (var pair in RequiredAttributes(id)) g_hash_table_insert(table, g_strdup(pair.Key), g_strdup(pair.Value));
                return table;
            }
            finally { NativeLibrary.Free(library); }
        }

        private static void FreeMatches(nint list)
        {
            for (var node = list; node != 0; node = Marshal.ReadIntPtr(node, IntPtr.Size)) g_object_unref(Marshal.ReadIntPtr(node));
            g_list_free(list);
        }
    }
}
