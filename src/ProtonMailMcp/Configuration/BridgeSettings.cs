using System.Net;

namespace ProtonMailMcp.Configuration;

internal enum BridgeTlsMode { StartTls, SslOnConnect }

internal sealed class ConfigurationDocument
{
    public string Host { get; init; } = "127.0.0.1";
    public int Port { get; init; }
    public string? TlsMode { get; init; }
    public string? Username { get; init; }
    public string? PasswordFile { get; init; }
    public string? CertificateFile { get; init; }
    public string[]? AllowedFolders { get; init; }
}

// Deliberately not a record: generated ToString must never disclose credentials/settings.
internal sealed class BridgeSettings(
    IPAddress host, int port, BridgeTlsMode tlsMode, string username,
    string password, byte[] certificateDer, IEnumerable<string> allowedFolders)
{
    public IPAddress Host { get; } = host;
    public int Port { get; } = port;
    public BridgeTlsMode TlsMode { get; } = tlsMode;
    public string Username { get; } = username;
    public string Password { get; } = password;
    public ReadOnlyMemory<byte> CertificateDer { get; } = certificateDer;
    public IReadOnlyList<string> AllowedFolders { get; } = Array.AsReadOnly(allowedFolders.ToArray());

    public bool IsFolderAllowed(string folder) => AllowedFolders.Any(allowed =>
        string.Equals(allowed, folder, StringComparison.Ordinal) ||
        (string.Equals(allowed, "INBOX", StringComparison.OrdinalIgnoreCase) &&
         string.Equals(folder, "INBOX", StringComparison.OrdinalIgnoreCase)));
}
