using System.Runtime.InteropServices;

namespace ProtonMailMcp.Credentials;

// ABI: GLib gboolean/gint = 32 bits, GType/gsize = native unsigned size,
// GList = data,next,prev pointers. All callbacks use C calling convention.
// Audited against libsecret 0.21.4 headers and GLib 2.80; no vendored native code.
internal static class SecretNative
{
    internal const string GLib = "libglib-2.0.so.0";
    private const string Gio = "libgio-2.0.so.0", Object = "libgobject-2.0.so.0", Secret = "libsecret-1.so.0";
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void Ready(nint source, nint result, nint data);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void Signal(nint connection, nint sender, nint path, nint iface, nint name, nint parameters, nint data);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void Destroy(nint data);
    [StructLayout(LayoutKind.Sequential)] internal struct NativeError { internal uint Domain; internal int Code; internal nint Message; }
    [DllImport(GLib, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern nint g_main_context_new();
    [DllImport(GLib, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern void g_main_context_push_thread_default(nint context);
    [DllImport(GLib, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern void g_main_context_pop_thread_default(nint context);
    [DllImport(GLib, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern int g_main_context_iteration(nint context, int block);
    [DllImport(GLib, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern int g_main_context_pending(nint context);
    [DllImport(GLib, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern void g_main_context_unref(nint context);
    [DllImport(GLib, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern void g_error_free(nint error);
    [DllImport(GLib, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern void g_list_free(nint list);
    [DllImport(GLib, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern nint g_strdup(string text);
    [DllImport(GLib, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern int g_strcmp0(nint left, string right);
    [DllImport(GLib, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern nint g_hash_table_new_full(nint hash, nint equal, nint keyFree, nint valueFree);
    [DllImport(GLib, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern int g_hash_table_insert(nint table, nint key, nint value);
    [DllImport(GLib, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern nint g_hash_table_lookup(nint table, string key);
    [DllImport(GLib, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern void g_hash_table_unref(nint table);
    [DllImport(GLib, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern nint g_variant_new_string(string value);
    [DllImport(GLib, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern nint g_variant_new_tuple(nint[] children, nuint length);
    [DllImport(GLib, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern nint g_variant_get_child_value(nint value, nuint index);
    [DllImport(GLib, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern nint g_variant_get_string(nint value, out nuint length);
    [DllImport(GLib, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern int g_variant_get_boolean(nint value);
    [DllImport(GLib, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern void g_variant_unref(nint value);
    [DllImport(Object, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern void g_object_unref(nint value);
    [DllImport(Gio, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern nint g_cancellable_new();
    [DllImport(Gio, CallingConvention = CallingConvention.Cdecl)]
    internal static extern uint g_io_error_quark();
    [DllImport(Gio, CallingConvention = CallingConvention.Cdecl)]
    internal static extern uint g_dbus_error_quark();
    [DllImport(Gio, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern void g_cancellable_cancel(nint cancel);
    [DllImport(Gio, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern void g_bus_get(int type, nint cancel, Ready callback, nint data);
    [DllImport(Gio, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern nint g_bus_get_finish(nint result, out nint error);
    [DllImport(Gio, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern nint g_dbus_proxy_get_connection(nint proxy);
    [DllImport(Gio, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern nint g_dbus_proxy_get_object_path(nint proxy);
    [DllImport(Gio, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern void g_dbus_connection_set_exit_on_close(nint connection, int exit);
    [DllImport(Gio, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern uint g_dbus_connection_signal_subscribe(nint connection, string sender, string iface, string member, string path, string arg0, int flags, Signal callback, nint data, Destroy destroy);
    [DllImport(Gio, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern void g_dbus_connection_signal_unsubscribe(nint connection, uint subscription);
    [DllImport(Gio, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern void g_dbus_connection_call(nint connection, string name, string path, string iface, string method, nint parameters, nint replyType, int flags, int timeout, nint cancel, Ready callback, nint data);
    [DllImport(Gio, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern nint g_dbus_connection_call_finish(nint connection, nint result, out nint error);
    [DllImport(Gio, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern void g_dbus_connection_close(nint connection, nint cancel, Ready callback, nint data);
    [DllImport(Gio, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern int g_dbus_connection_close_finish(nint connection, nint result, out nint error);
    [DllImport(Secret, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern nuint secret_service_get_type();
    [DllImport(Secret, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern uint secret_error_get_quark();
    [DllImport(Secret, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern void secret_service_open(nuint type, string? name, int flags, nint cancel, Ready callback, nint data);
    [DllImport(Secret, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern nint secret_service_open_finish(nint result, out nint error);
    [DllImport(Secret, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern void secret_collection_for_alias(nint service, string alias, int flags, nint cancel, Ready callback, nint data);
    [DllImport(Secret, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern nint secret_collection_for_alias_finish(nint result, out nint error);
    [DllImport(Secret, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern int secret_collection_get_locked(nint collection);
    [DllImport(Secret, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern void secret_collection_search(nint collection, nint schema, nint attributes, int flags, nint cancel, Ready callback, nint data);
    [DllImport(Secret, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern nint secret_collection_search_finish(nint collection, nint result, out nint error);
    [DllImport(Secret, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern nint secret_item_get_attributes(nint item);
    [DllImport(Secret, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern int secret_item_get_locked(nint item);
    [DllImport(Secret, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern void secret_item_load_secret(nint item, nint cancel, Ready callback, nint data);
    [DllImport(Secret, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern int secret_item_load_secret_finish(nint item, nint result, out nint error);
    [DllImport(Secret, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern nint secret_item_get_secret(nint item);
    [DllImport(Secret, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern nint secret_value_get(nint value, out nuint length);
    [DllImport(Secret, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern nint secret_value_get_content_type(nint value);
    [DllImport(Secret, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern void secret_value_unref(nint value);
}
