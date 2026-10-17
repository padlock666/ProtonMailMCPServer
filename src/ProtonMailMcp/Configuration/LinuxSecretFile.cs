using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ProtonMailMcp.Configuration;

internal static partial class LinuxSecretFile
{
    private const int NoFollow = 0x20000, CloseOnExec = 0x80000, NonBlock = 0x800, Directory = 0x10000;

    public static FileStream Open(string path)
    {
        if (!OperatingSystem.IsLinux())
            throw new ConfigurationException("configuration_linux_required");

        // Parent directory must also be private. The handle, not path metadata, is checked.
        using var parent = OpenHandle(Path.GetDirectoryName(path)!, Directory);
        var directory = Inspect(parent);
        if ((directory.Mode & 0xF000) != 0x4000 || !IsPrivate(directory))
            throw new ConfigurationException("configuration_password_directory_unsafe");

        var fd = OpenAtNative(parent, Path.GetFileName(path), NoFollow | CloseOnExec | NonBlock);
        if (fd < 0) throw new ConfigurationException("configuration_password_file_unavailable");
        var handle = new SafeFileHandle(fd, ownsHandle: true);
        try
        {
            var file = Inspect(handle);
            if ((file.Mode & 0xF000) != 0x8000 || !IsPrivate(file) ||
                (file.Mode & 0x100) == 0 || file.Size is 0 or > 4096)
                throw new ConfigurationException("configuration_password_file_unsafe");
            return new FileStream(handle, FileAccess.Read);
        }
        catch { handle.Dispose(); throw; }
    }

    private static bool IsPrivate(Statx info) => info.Uid == GetEffectiveUserId() && (info.Mode & 0xE3F) == 0;

    private static SafeFileHandle OpenHandle(string path, int flags)
    {
        var fd = OpenNative(path, flags | NoFollow | CloseOnExec | NonBlock);
        if (fd < 0) throw new ConfigurationException("configuration_password_file_unavailable");
        return new SafeFileHandle(fd, ownsHandle: true);
    }

    private static Statx Inspect(SafeFileHandle handle)
    {
        // statx ABI is fixed across Linux architectures; AT_EMPTY_PATH inspects this open fd.
        if (StatxNative(handle, "", 0x1000, 0x20F, out var info) != 0 || (info.Mask & 0x20F) != 0x20F)
            throw new ConfigurationException("configuration_password_metadata_unavailable");
        return info;
    }

    [StructLayout(LayoutKind.Explicit, Size = 256)]
    private struct Statx
    {
        [FieldOffset(0)] public uint Mask;
        [FieldOffset(20)] public uint Uid;
        [FieldOffset(28)] public ushort Mode;
        [FieldOffset(40)] public ulong Size;
    }

    [LibraryImport("libc", EntryPoint = "open", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int OpenNative(string path, int flags);
    [LibraryImport("libc", EntryPoint = "openat", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int OpenAtNative(SafeFileHandle directory, string path, int flags);
    [LibraryImport("libc", EntryPoint = "statx", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int StatxNative(SafeFileHandle fd, string path, int flags, uint mask, out Statx info);
    [LibraryImport("libc", EntryPoint = "geteuid")]
    private static partial uint GetEffectiveUserId();
}
