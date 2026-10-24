using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ProtonMailMcp.Configuration;

internal static partial class LinuxProtectedFile
{
    private const int NoFollow = 0x20000, CloseOnExec = 0x80000, NonBlock = 0x800, Directory = 0x10000;

    internal enum FileKind { Password, Configuration, Certificate }

    internal static void ValidateDownloadPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || path.EndsWith('/') ||
            path.Any(char.IsControl) || System.Text.Encoding.UTF8.GetByteCount(path) > 3000 ||
            path.Split('/').Any(part => part is "." or ".."))
            throw new ConfigurationException("configuration_attachment_downloads_invalid");
        // Canonical paths avoid distinct spellings of the held directory.
        if (path.Contains("//", StringComparison.Ordinal))
            throw new ConfigurationException("configuration_attachment_downloads_invalid");
    }

    internal static SafeFileHandle OpenDownloadDirectory(string path)
    {
        ValidateDownloadPath(path);
        SafeFileHandle? parent = null;
        try
        {
            parent = OpenHandle("/", Directory, FileKind.Configuration);
            RequireTrustedAncestor(parent);
            foreach (var part in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                var next = OpenAt(parent, part, Directory, FileKind.Configuration);
                parent.Dispose();
                parent = next;
                RequireTrustedAncestor(parent);
            }
            var info = Inspect(parent);
            if (info.Uid != GetEffectiveUserId() || (info.Mode & 0xFFFF) != 0x41C0)
                throw new ConfigurationException("configuration_download_directory_unsafe");
            var result = parent;
            parent = null;
            return result;
        }
        catch (ConfigurationException)
        {
            throw new ConfigurationException("configuration_download_directory_unsafe");
        }
        finally { parent?.Dispose(); }
    }

    public static FileStream Open(string path, FileKind kind = FileKind.Password)
    {
        if (!OperatingSystem.IsLinux())
            throw new ConfigurationException("configuration_linux_required");
        if (!Path.IsPathFullyQualified(path))
            throw new ConfigurationException("configuration_absolute_path_required");

        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts.Any(part => part is "." or ".."))
            throw new ConfigurationException("configuration_path_unsafe");

        // Traverse from root by directory handle, so no ancestor symlink or path replacement
        // can redirect the file between the permission checks and the read.
        var parent = OpenHandle("/", Directory, kind);
        try
        {
            RequireTrustedAncestor(parent);
            for (var index = 0; index < parts.Length - 1; index++)
            {
                var next = OpenAt(parent, parts[index], Directory, kind);
                parent.Dispose();
                parent = next;
                if (index == parts.Length - 2) RequirePrivateDirectory(parent, kind);
                else RequireTrustedAncestor(parent);
            }

            // The root directory cannot be the private parent of a configuration file.
            if (parts.Length == 1) RequirePrivateDirectory(parent, kind);

            var handle = OpenAt(parent, parts[^1], 0, kind);
            try
            {
                var file = Inspect(handle);
                if ((file.Mode & 0xF000) != 0x8000 || !IsPrivate(file) ||
                    (file.Mode & 0x100) == 0 || (kind == FileKind.Password && file.Size is 0 or > 4096))
                    throw new ConfigurationException(FileCode(kind, "file_unsafe"));
                return new FileStream(handle, FileAccess.Read);
            }
            catch { handle.Dispose(); throw; }
        }
        finally { parent.Dispose(); }
    }

    private static bool IsPrivate(Statx info) => info.Uid == GetEffectiveUserId() && (info.Mode & 0xE3F) == 0;

    private static void RequirePrivateDirectory(SafeFileHandle handle, FileKind kind)
    {
        var directory = Inspect(handle);
        if ((directory.Mode & 0xF000) != 0x4000 || !IsPrivate(directory))
            throw new ConfigurationException(FileCode(kind, "directory_unsafe"));
    }

    private static void RequireTrustedAncestor(SafeFileHandle handle)
    {
        var directory = Inspect(handle);
        // A root-owned sticky directory such as /tmp prevents other users from
        // replacing a private child directory, despite its world-write bit.
        var rootSticky = directory.Uid == 0 && (directory.Mode & 0x200) != 0;
        if ((directory.Mode & 0xF000) != 0x4000 ||
            directory.Uid != 0 && directory.Uid != GetEffectiveUserId() ||
            (directory.Mode & 0x12) != 0 && !rootSticky)
            throw new ConfigurationException("configuration_path_unsafe");
    }

    private static string FileCode(FileKind kind, string suffix) => kind switch
    {
        FileKind.Password => "configuration_password_" + suffix,
        FileKind.Configuration => "configuration_settings_" + suffix,
        FileKind.Certificate => "configuration_certificate_" + suffix,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static SafeFileHandle OpenHandle(string path, int flags, FileKind kind)
    {
        var fd = OpenNative(path, flags | NoFollow | CloseOnExec | NonBlock);
        if (fd < 0) throw new ConfigurationException(FileCode(kind, "file_unavailable"));
        return new SafeFileHandle(fd, ownsHandle: true);
    }

    private static SafeFileHandle OpenAt(SafeFileHandle parent, string name, int flags, FileKind kind)
    {
        var fd = OpenAtNative(parent, name, flags | NoFollow | CloseOnExec | NonBlock);
        if (fd < 0) throw new ConfigurationException(FileCode(kind, "file_unavailable"));
        return new SafeFileHandle(fd, ownsHandle: true);
    }

    internal static Statx Inspect(SafeFileHandle handle)
    {
        // statx ABI is fixed across Linux architectures; AT_EMPTY_PATH inspects this open fd.
        if (StatxNative(handle, "", 0x1000, 0x30F, out var info) != 0 || (info.Mask & 0x30F) != 0x30F)
            throw new ConfigurationException("configuration_metadata_unavailable");
        return info;
    }

    [StructLayout(LayoutKind.Explicit, Size = 256)]
    internal struct Statx
    {
        [FieldOffset(0)] public uint Mask;
        [FieldOffset(16)] public uint Links;
        [FieldOffset(20)] public uint Uid;
        [FieldOffset(28)] public ushort Mode;
        [FieldOffset(40)] public ulong Size;
        [FieldOffset(32)] public ulong Inode;
        [FieldOffset(136)] public uint DeviceMajor;
        [FieldOffset(140)] public uint DeviceMinor;
    }

    [LibraryImport("libc", EntryPoint = "open", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int OpenNative(string path, int flags);
    [LibraryImport("libc", EntryPoint = "openat", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int OpenAtNative(SafeFileHandle directory, string path, int flags);
    [LibraryImport("libc", EntryPoint = "statx", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int StatxNative(SafeFileHandle fd, string path, int flags, uint mask, out Statx info);
    [LibraryImport("libc", EntryPoint = "geteuid")]
    internal static partial uint GetEffectiveUserId();
}
