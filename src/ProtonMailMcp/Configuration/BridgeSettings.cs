using System.Net;

namespace ProtonMailMcp.Configuration;

internal enum BridgeTlsMode { StartTls, SslOnConnect }
internal enum FolderAccessMode { Allowlist, AllExcept }

internal sealed class FolderAccessDocument
{
    public string? Mode { get; init; }
    public string[]? AllowedFolders { get; init; }
    public string[]? ExcludedFolders { get; init; }
}

internal sealed class ConfigurationDocument
{
    public string Host { get; init; } = "127.0.0.1";
    public int Port { get; init; }
    public string? TlsMode { get; init; }
    public string? Username { get; init; }
    public string? PasswordFile { get; init; }
    public string? CertificateFile { get; init; }
    public string[]? AllowedFolders { get; init; }
    public FolderAccessDocument? FolderAccess { get; init; }
    public AttachmentDownloadsDocument? AttachmentDownloads { get; init; }
}

internal sealed class AttachmentDownloadsDocument
{
    public string? Directory { get; init; }
}

// Deliberately not a record: generated ToString must never disclose credentials/settings.
internal sealed class BridgeSettings(
    IPAddress host, int port, BridgeTlsMode tlsMode, string username,
    string password, byte[] certificateDer, IEnumerable<string> allowedFolders,
    FolderAccessMode folderAccessMode = FolderAccessMode.Allowlist, IEnumerable<string>? excludedFolders = null,
    string? downloadDirectory = null)
{
    public IPAddress Host { get; } = host;
    public int Port { get; } = port;
    public BridgeTlsMode TlsMode { get; } = tlsMode;
    public string Username { get; } = username;
    public string Password { get; } = password;
    public ReadOnlyMemory<byte> CertificateDer { get; } = certificateDer;
    public IReadOnlyList<string> AllowedFolders { get; } = Array.AsReadOnly(allowedFolders.ToArray());
    public FolderAccessMode FolderAccessMode { get; } = folderAccessMode;
    public IReadOnlyList<string> ExcludedFolders { get; } = Array.AsReadOnly(excludedFolders?.ToArray() ?? []);
    public string? DownloadDirectory { get; } = downloadDirectory;

    public bool IsFolderAllowed(string folder) => FolderAccessMode switch
    {
        FolderAccessMode.Allowlist => AllowedFolders.Any(allowed => FolderNamesEqual(allowed, folder)),
        FolderAccessMode.AllExcept => AllowedFolders.Count == 0 &&
            !ExcludedFolders.Any(excluded => FolderNamesEqual(excluded, folder)),
        _ => false
    };

    internal static bool FolderNamesEqual(string left, string right) =>
        string.Equals(left, right, StringComparison.Ordinal) ||
        (string.Equals(left, "INBOX", StringComparison.OrdinalIgnoreCase) &&
         string.Equals(right, "INBOX", StringComparison.OrdinalIgnoreCase));
}
