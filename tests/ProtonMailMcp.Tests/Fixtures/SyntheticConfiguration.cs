using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace ProtonMailMcp.Tests.Fixtures;

internal sealed class SyntheticConfiguration : IDisposable
{
    public const string SentinelPassword = "SYNTHETIC-password-sentinel";
    public const string SentinelUsername = "SYNTHETIC-username-sentinel";
    public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "protonmcp-test-" + Guid.NewGuid().ToString("N"));
    public string ConfigPath => Path.Combine(DirectoryPath, "config.json");
    public string FixturePath => Path.Combine(DirectoryPath, "secret");
    public string CertificatePath => Path.Combine(DirectoryPath, "public.pem");
    public Dictionary<string, object?> Values { get; }

    public SyntheticConfiguration()
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Tests require Linux.");
        Directory.CreateDirectory(DirectoryPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        File.WriteAllText(FixturePath, "SYNTHETIC-file-content");
        File.SetUnixFileMode(FixturePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        WriteCertificate(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        Values = new()
        {
            ["Port"] = 1143, ["TlsMode"] = "StartTls", ["Username"] = SentinelUsername,
            ["Keyring"] = new { Application = "ProtonMailMcp", Account = "BrigePassword" }, ["CertificateFile"] = CertificatePath,
            ["AllowedFolders"] = new[] { "INBOX", "Approved" }
        };
        Save();
    }

    public string EnableDownloads()
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
        var path = Path.Combine(DirectoryPath, "downloads");
        Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        Values["AttachmentDownloads"] = new { Directory = path };
        Save();
        return path;
    }

    public void Save()
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Tests require Linux.");
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(Values));
        File.SetUnixFileMode(ConfigPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    public void WriteCertificate(DateTimeOffset start, DateTimeOffset end, bool der = false)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Tests require Linux.");
        using var key = ECDsa.Create();
        var request = new CertificateRequest("CN=synthetic.invalid", key, HashAlgorithmName.SHA256);
        using var cert = request.CreateSelfSigned(start, end);
        if (der) File.WriteAllBytes(CertificatePath, cert.RawData);
        else File.WriteAllText(CertificatePath, cert.ExportCertificatePem());
        File.SetUnixFileMode(CertificatePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    private IsolatedKeyring? keyring;
    public async Task<Dictionary<string, string?>> KeyringEnvironmentAsync()
    {
        keyring ??= await IsolatedKeyring.StartAsync();
        return keyring.Environment.ToDictionary(p => p.Key, p => (string?)p.Value);
    }

    public void Dispose()
    {
        try { keyring?.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
        finally { Directory.Delete(DirectoryPath, recursive: true); }
    }
}
