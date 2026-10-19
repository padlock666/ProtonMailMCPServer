using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProtonMailMcp.Configuration;

internal static class ConfigurationLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static async Task<BridgeSettings> LoadAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            RequireAbsolutePath(path);
            await using var config = LinuxProtectedFile.Open(path, LinuxProtectedFile.FileKind.Configuration);
            var bytes = await ReadBoundedAsync(config, 65536, cancellationToken);
            using var json = JsonDocument.Parse(bytes, new JsonDocumentOptions { AllowDuplicateProperties = false });
            var document = json.RootElement.Deserialize<ConfigurationDocument>(JsonOptions)
                ?? throw new ConfigurationException("configuration_invalid");
            if (string.IsNullOrWhiteSpace(document.Host) || !IPAddress.TryParse(document.Host, out var host) ||
                !IPAddress.IsLoopback(host) || document.Host.Contains('%') ||
                (host.AddressFamily == AddressFamily.InterNetwork && document.Host != host.ToString()))
                throw new ConfigurationException("configuration_host_invalid");
            if (document.Port is < 1 or > 65535)
                throw new ConfigurationException("configuration_port_invalid");
            var tlsMode = document.TlsMode switch
            {
                "StartTls" => BridgeTlsMode.StartTls,
                "SslOnConnect" => BridgeTlsMode.SslOnConnect,
                _ => throw new ConfigurationException("configuration_tls_mode_invalid")
            };
            if (string.IsNullOrWhiteSpace(document.Username) || document.Username.Any(char.IsControl))
                throw new ConfigurationException("configuration_username_invalid");
            var (folderMode, allowedFolders, excludedFolders) = ReadFolderAccess(document, json.RootElement);
            RequireAbsolutePath(document.CertificateFile);
            RequireAbsolutePath(document.PasswordFile);
            var certificateDer = await LoadCertificateAsync(document.CertificateFile!, cancellationToken);
            await using var secret = LinuxProtectedFile.Open(document.PasswordFile!);
            var password = new UTF8Encoding(false, true).GetString(await ReadBoundedAsync(secret, 4096, cancellationToken));
            // Accept one conventional line ending, but never trim spaces from a credential.
            if (password.EndsWith('\n')) password = password[..^1];
            if (password.EndsWith('\r')) password = password[..^1];
            if (string.IsNullOrWhiteSpace(password) || password.Any(char.IsControl))
                throw new ConfigurationException("configuration_password_invalid");
            return new BridgeSettings(host, document.Port, tlsMode, document.Username, password,
                certificateDer, allowedFolders, folderMode, excludedFolders);
        }
        catch (OperationCanceledException) { throw; }
        catch (ConfigurationException) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or
            CryptographicException or ArgumentException or NotSupportedException)
        {
            throw new ConfigurationException("configuration_invalid_or_unavailable");
        }
    }

    private static (FolderAccessMode Mode, string[] Allowed, string[] Excluded) ReadFolderAccess(
        ConfigurationDocument document, JsonElement root)
    {
        if (!root.TryGetProperty("FolderAccess", out var policy))
        {
            ValidateFolderNames(document.AllowedFolders, requireNonempty: true, "configuration_allowlist_invalid");
            return (FolderAccessMode.Allowlist, document.AllowedFolders!, []);
        }

        const string error = "configuration_folder_access_invalid";
        if (root.TryGetProperty("AllowedFolders", out _) || document.FolderAccess is not { } access)
            throw new ConfigurationException(error);
        switch (access.Mode)
        {
            case "Allowlist":
                if (policy.TryGetProperty("ExcludedFolders", out _)) throw new ConfigurationException(error);
                ValidateFolderNames(access.AllowedFolders, requireNonempty: true, error);
                return (FolderAccessMode.Allowlist, access.AllowedFolders!, []);
            case "AllExcept":
                if (policy.TryGetProperty("AllowedFolders", out _)) throw new ConfigurationException(error);
                ValidateFolderNames(access.ExcludedFolders, requireNonempty: false, error);
                return (FolderAccessMode.AllExcept, [], access.ExcludedFolders!);
            default:
                throw new ConfigurationException(error);
        }
    }

    private static void ValidateFolderNames(string[]? names, bool requireNonempty, string error)
    {
        if (names is null || (requireNonempty && names.Length == 0) || names.Length > 256 ||
            names.Any(f => string.IsNullOrWhiteSpace(f) || f.Length > 1024 ||
                f.Any(char.IsControl) || f.Contains('*') || f.Contains('%')))
            throw new ConfigurationException(error);
    }

    private static void RequireAbsolutePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new ConfigurationException("configuration_absolute_path_required");
    }

    private static async Task<byte[]> LoadCertificateAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = LinuxProtectedFile.Open(path, LinuxProtectedFile.FileKind.Certificate);
        var bytes = await ReadBoundedAsync(stream, 65536, cancellationToken);
        X509Certificate2 certificate;
        if (bytes.AsSpan().StartsWith("-----BEGIN"u8))
        {
            var pem = Encoding.ASCII.GetString(bytes);
            const string begin = "-----BEGIN CERTIFICATE-----", end = "-----END CERTIFICATE-----";
            var trimmed = pem.Trim();
            if (!trimmed.StartsWith(begin, StringComparison.Ordinal) ||
                !trimmed.EndsWith(end, StringComparison.Ordinal) ||
                trimmed.IndexOf("-----BEGIN", begin.Length, StringComparison.Ordinal) >= 0)
                throw new ConfigurationException("configuration_public_certificate_required");
            certificate = X509Certificate2.CreateFromPem(trimmed);
        }
        else
        {
            certificate = X509CertificateLoader.LoadCertificate(bytes);
            if (!certificate.RawData.AsSpan().SequenceEqual(bytes))
            {
                certificate.Dispose();
                throw new ConfigurationException("configuration_public_certificate_required");
            }
        }
        using (certificate)
        {
            var now = DateTime.UtcNow;
            if (certificate.HasPrivateKey || now < certificate.NotBefore.ToUniversalTime() || now > certificate.NotAfter.ToUniversalTime())
                throw new ConfigurationException("configuration_certificate_invalid");
            return certificate.RawData;
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(Stream stream, int limit, CancellationToken cancellationToken)
    {
        var buffer = new byte[limit + 1];
        var count = 0;
        while (count < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(count), cancellationToken);
            if (read == 0) break;
            count += read;
        }
        if (count > limit) throw new ConfigurationException("configuration_file_too_large");
        return buffer[..count];
    }
}
