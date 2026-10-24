using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;
using ProtonMailMcp.Configuration;
using ProtonMailMcp.Contracts;

namespace ProtonMailMcp.Mail;

// Linux handle-relative operations; no attachment name or caller path reaches the filesystem.
internal sealed partial class PrivateDownloadStore : IDisposable
{
    internal const int MaxEncodedBytes = 36 * 1024 * 1024;
    internal const int MaxFileBytes = 25 * 1024 * 1024;
    internal const long MaxDirectoryBytes = 250L * 1024 * 1024;
    internal const int MaxEntries = 1000;
    private const int DirectoryFlags = 0x10000 | 0x20000 | 0x80000;
    private readonly SafeFileHandle directory;
    private readonly LinuxProtectedFile.Statx identity;
    private readonly string path;
    internal Func<string> NameFactory { get; init; } = RandomName;

    public PrivateDownloadStore(string path)
    {
        if (!OperatingSystem.IsLinux() || !Environment.Is64BitProcess)
            throw new ConfigurationException("configuration_linux_required");
        this.path = path;
        directory = LinuxProtectedFile.OpenDownloadDirectory(path);
        try
        {
            identity = LinuxProtectedFile.Inspect(directory);
            if (Flock(directory, 2 | 4) != 0)
                throw new ConfigurationException("configuration_download_directory_in_use");
        }
        catch { directory.Dispose(); throw; }
    }

    internal void Check(bool reserve, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        try
        {
            using var current = LinuxProtectedFile.OpenDownloadDirectory(path);
            if (!Same(identity, LinuxProtectedFile.Inspect(current))) Unsafe();
            // fdopendir owns a separate open file description, not the lifetime lock handle.
            var fd = OpenAt(directory, ".", DirectoryFlags, 0);
            if (fd < 0) Storage();
            var entries = FdOpenDir(fd);
            if (entries == IntPtr.Zero) { Close(fd); Storage(); }
            try
            {
                long bytes = 0;
                var count = 0;
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    Marshal.SetLastPInvokeError(0);
                    var entry = ReadDir(entries);
                    if (entry == IntPtr.Zero)
                    {
                        if (Marshal.GetLastPInvokeError() != 0) Storage();
                        break;
                    }
                    // Linux 64-bit dirent ABI; reject unavailable metadata rather than follow paths.
                    var name = Marshal.PtrToStringUTF8(entry + 19)!;
                    if (name is "." or "..") continue;
                    if (++count > MaxEntries) throw new MailboxException(MailboxError.DownloadLimitExceeded);
                    if (StatAt(directory, name, 0x100, 0x30F, out var info) != 0 ||
                        (info.Mask & 0x30F) != 0x30F || !PrivateFile(info)) Unsafe();
                    if (info.Size > (ulong)MaxDirectoryBytes) throw new MailboxException(MailboxError.DownloadLimitExceeded);
                    bytes += (long)info.Size;
                    if (bytes > MaxDirectoryBytes) throw new MailboxException(MailboxError.DownloadLimitExceeded);
                }
                if (count + (reserve ? 1 : 0) > MaxEntries || bytes + (reserve ? MaxFileBytes : 0) > MaxDirectoryBytes)
                    throw new MailboxException(MailboxError.DownloadLimitExceeded);
            }
            finally { CloseDir(entries); }
        }
        catch (ConfigurationException) { Unsafe(); }
    }

    internal TemporaryFile Create()
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var name = ".attachment-" + NameFactory() + ".part";
            var fd = OpenAt(directory, name, 2 | 0x40 | 0x80 | 0x20000 | 0x80000 | 0x800, 0x180);
            if (fd < 0)
            {
                if (Marshal.GetLastPInvokeError() == 17) continue;
                Storage();
            }
            var handle = new SafeFileHandle(fd, true);
            try
            {
                // A restrictive umask must not make a completed file unreadable by its owner.
                if (Chmod(handle, 0x180) != 0) Storage();
                var info = LinuxProtectedFile.Inspect(handle);
                if (!PrivateFile(info) || (info.Mode & 0xFFF) != 0x180) Unsafe();
                return new TemporaryFile(this, name, info, new FileStream(handle, FileAccess.ReadWrite));
            }
            catch
            {
                handle.Dispose();
                if (UnlinkAt(directory, name, 0) != 0) throw new MailboxException(MailboxError.DownloadCleanupFailed);
                throw;
            }
        }
        throw new MailboxException(MailboxError.DownloadStorageFailed);
    }

    internal sealed class TemporaryFile(PrivateDownloadStore owner, string name,
        LinuxProtectedFile.Statx identity, FileStream stream)
    {
        internal FileStream Stream { get; } = stream;
        private bool published;

        internal string Publish(string extension, CancellationToken token)
        {
            owner.Check(false, token);
            if (StatAt(owner.directory, name, 0x100, 0x30F, out var info) != 0 ||
                !Same(identity, info) || !PrivateFile(info)) Unsafe();
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var final = "attachment-" + owner.NameFactory() + extension;
                var result = Path.Combine(owner.path, final); // prepare before the commit point
                token.ThrowIfCancellationRequested();
                if (RenameAt(owner.directory, name, owner.directory, final, 1) == 0)
                {
                    published = true;
                    return result;
                }
                if (Marshal.GetLastPInvokeError() != 17) Storage();
            }
            throw new MailboxException(MailboxError.DownloadStorageFailed);
        }

        internal void Cleanup()
        {
            try { Stream.Dispose(); }
            catch (IOException) { /* Still attempt unlink of our temporary file. */ }
            if (published) return;
            if (StatAt(owner.directory, name, 0x100, 0x30F, out var info) != 0 ||
                !Same(identity, info) || UnlinkAt(owner.directory, name, 0) != 0)
                throw new MailboxException(MailboxError.DownloadCleanupFailed);
        }
    }

    private static string RandomName() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
    private static bool Same(LinuxProtectedFile.Statx a, LinuxProtectedFile.Statx b) =>
        a.Inode == b.Inode && a.DeviceMajor == b.DeviceMajor && a.DeviceMinor == b.DeviceMinor;
    private static bool PrivateFile(LinuxProtectedFile.Statx info) =>
        (info.Mode & 0xF000) == 0x8000 && (info.Mode & 0xE7F) == 0 && (info.Mode & 0x100) != 0 &&
        info.Uid == LinuxProtectedFile.GetEffectiveUserId() && info.Links == 1;
    private static void Unsafe() => throw new MailboxException(MailboxError.DownloadDirectoryUnsafe);
    private static void Storage() => throw new MailboxException(MailboxError.DownloadStorageFailed);
    public void Dispose() => directory.Dispose();

    [LibraryImport("libc", EntryPoint = "openat", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int OpenAt(SafeFileHandle dir, string name, int flags, uint mode);
    [LibraryImport("libc", EntryPoint = "statx", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int StatAt(SafeFileHandle dir, string name, int flags, uint mask, out LinuxProtectedFile.Statx info);
    [LibraryImport("libc", EntryPoint = "flock")]
    private static partial int Flock(SafeFileHandle fd, int operation);
    [LibraryImport("libc", EntryPoint = "fchmod")]
    private static partial int Chmod(SafeFileHandle fd, uint mode);
    [LibraryImport("libc", EntryPoint = "renameat2", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int RenameAt(SafeFileHandle oldDir, string oldName, SafeFileHandle newDir, string newName, uint flags);
    [LibraryImport("libc", EntryPoint = "unlinkat", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int UnlinkAt(SafeFileHandle dir, string name, int flags);
    [LibraryImport("libc", EntryPoint = "fdopendir")]
    private static partial IntPtr FdOpenDir(int fd);
    [LibraryImport("libc", EntryPoint = "readdir", SetLastError = true)]
    private static partial IntPtr ReadDir(IntPtr directory);
    [LibraryImport("libc", EntryPoint = "closedir")]
    private static partial int CloseDir(IntPtr directory);
    [LibraryImport("libc", EntryPoint = "close")]
    private static partial int Close(int fd);
}
