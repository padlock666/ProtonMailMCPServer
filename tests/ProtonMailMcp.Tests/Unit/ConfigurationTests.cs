using ProtonMailMcp.Configuration;
using ProtonMailMcp.Tests.Fixtures;

namespace ProtonMailMcp.Tests.Unit;

[System.Runtime.Versioning.SupportedOSPlatform("linux")]
[Trait("Category", "Unit")]
public sealed class ConfigurationTests
{
    [Theory]
    [InlineData(null, "StartTls")]
    [InlineData("127.0.0.1", "SslOnConnect")]
    [InlineData("::1", "StartTls")]
    public async Task LoadsExplicitSafeSettings(string? host, string tls)
    {
        using var fixture = new SyntheticConfiguration();
        if (host is not null) fixture.Values["Host"] = host;
        fixture.Values["TlsMode"] = tls;
        fixture.Save();
        var settings = await ConfigurationLoader.LoadAsync(fixture.ConfigPath, CancellationToken.None);
        Assert.Equal(host ?? "127.0.0.1", settings.Host.ToString());
        Assert.Equal(SyntheticConfiguration.SentinelPassword, settings.Password);
        Assert.NotEmpty(settings.CertificateDer.ToArray());
        Assert.DoesNotContain(SyntheticConfiguration.SentinelPassword, settings.ToString());
        Assert.True(settings.IsFolderAllowed("inbox"));
        Assert.True(settings.IsFolderAllowed("Approved"));
        Assert.False(settings.IsFolderAllowed("approved"));
        Assert.False(settings.IsFolderAllowed("Approved/Child"));
        Assert.False(settings.IsFolderAllowed("Forbidden"));
    }

    public static TheoryData<string, object?> InvalidFields => new()
    {
        { "Host", "localhost" }, { "Host", "192.0.2.1" }, { "Host", "0.0.0.0" },
        { "Host", "::" }, { "Host", "::ffff:192.0.2.1" }, { "Host", "::1%1" },
        { "Host", "127.1" }, { "Host", "0x7f000001" }, { "Host", "2130706433" },
        { "Host", null }, { "Port", 0 }, { "Port", 65536 },
        { "TlsMode", null }, { "TlsMode", "None" }, { "TlsMode", "StartTlsWhenAvailable" },
        { "TlsMode", "1" }, { "TlsMode", "starttls" },
        { "Username", "" }, { "Username", "synthetic\nvalue" },
        { "AllowedFolders", Array.Empty<string>() }, { "AllowedFolders", null },
        { "AllowedFolders", new[] { "" } }, { "AllowedFolders", new[] { "INBOX/*" } },
        { "AllowedFolders", new[] { "INBOX/%" } }, { "AllowedFolders", new[] { "Bad\nName" } },
        { "CertificateFile", "relative.pem" }, { "PasswordFile", "relative-secret" },
        { "Password", SyntheticConfiguration.SentinelPassword }
    };

    [Theory]
    [MemberData(nameof(InvalidFields))]
    public async Task RejectsInvalidFieldsWithoutEchoingData(string field, object? value)
    {
        using var fixture = new SyntheticConfiguration();
        fixture.Values[field] = value;
        fixture.Save();
        await AssertSafeFailure(fixture);
    }

    [Theory]
    [InlineData("Port")]
    [InlineData("TlsMode")]
    [InlineData("Username")]
    [InlineData("PasswordFile")]
    [InlineData("CertificateFile")]
    [InlineData("AllowedFolders")]
    public async Task RejectsMissingRequiredFields(string field)
    {
        using var fixture = new SyntheticConfiguration();
        fixture.Values.Remove(field);
        fixture.Save();
        await AssertSafeFailure(fixture);
    }

    [Theory]
    [InlineData(UnixFileMode.UserRead | UnixFileMode.GroupRead)]
    [InlineData(UnixFileMode.UserRead | UnixFileMode.OtherRead)]
    [InlineData(UnixFileMode.UserRead | UnixFileMode.GroupWrite)]
    [InlineData(UnixFileMode.UserWrite)]
    public async Task RejectsUnsafePasswordModes(UnixFileMode mode)
    {
        using var fixture = new SyntheticConfiguration();
        File.SetUnixFileMode(fixture.PasswordPath, mode);
        await AssertSafeFailure(fixture);
    }

    [Fact]
    public async Task RejectsNonPrivateParentDirectory()
    {
        using var fixture = new SyntheticConfiguration();
        File.SetUnixFileMode(fixture.DirectoryPath, UnixFileMode.UserRead | UnixFileMode.UserWrite |
            UnixFileMode.UserExecute | UnixFileMode.GroupExecute);
        await AssertSafeFailure(fixture);
    }

    [Fact]
    public async Task RejectsSymlinkAndDirectoryPasswordPaths()
    {
        using var fixture = new SyntheticConfiguration();
        var link = Path.Combine(fixture.DirectoryPath, "link");
        File.CreateSymbolicLink(link, fixture.PasswordPath);
        fixture.Values["PasswordFile"] = link;
        fixture.Save();
        await AssertSafeFailure(fixture);
        fixture.Values["PasswordFile"] = fixture.DirectoryPath;
        fixture.Save();
        await AssertSafeFailure(fixture);
    }

    [Fact]
    public async Task RejectsFifoWithoutBlocking()
    {
        using var fixture = new SyntheticConfiguration();
        File.Delete(fixture.PasswordPath);
        var info = new System.Diagnostics.ProcessStartInfo("mkfifo") { UseShellExecute = false };
        info.ArgumentList.Add(fixture.PasswordPath);
        using var process = System.Diagnostics.Process.Start(info)!;
        await process.WaitForExitAsync();
        Assert.Equal(0, process.ExitCode);
        await AssertSafeFailure(fixture);
    }

    [Fact]
    public async Task ReadsTheValidatedHandleAfterPathReplacement()
    {
        using var fixture = new SyntheticConfiguration();
        await using var stream = LinuxSecretFile.Open(fixture.PasswordPath);
        File.Move(fixture.PasswordPath, fixture.PasswordPath + ".old");
        File.WriteAllText(fixture.PasswordPath, "SYNTHETIC-replacement");
        using var reader = new StreamReader(stream);
        Assert.Equal(SyntheticConfiguration.SentinelPassword + "\n", await reader.ReadToEndAsync());
    }

    [Theory]
    [InlineData("")]
    [InlineData("\n")]
    [InlineData("synthetic\nextra")]
    [InlineData("synthetic\0")]
    public async Task RejectsInvalidPasswords(string password)
    {
        using var fixture = new SyntheticConfiguration();
        File.WriteAllText(fixture.PasswordPath, password);
        await AssertSafeFailure(fixture);
    }

    [Fact]
    public async Task RejectsOversizedAndInvalidUtf8Passwords()
    {
        using var fixture = new SyntheticConfiguration();
        File.WriteAllText(fixture.PasswordPath, new string('x', 4097));
        await AssertSafeFailure(fixture);
        File.WriteAllBytes(fixture.PasswordPath, [0xFF]);
        await AssertSafeFailure(fixture);
    }

    [Theory]
    [InlineData(-3, -1)]
    [InlineData(1, 3)]
    public async Task RejectsExpiredAndFutureCertificates(int start, int end)
    {
        using var fixture = new SyntheticConfiguration();
        fixture.WriteCertificate(DateTimeOffset.UtcNow.AddDays(start), DateTimeOffset.UtcNow.AddDays(end));
        await AssertSafeFailure(fixture);
    }

    [Fact]
    public async Task AcceptsDerAndRejectsUnusableOrPrivateCertificateMaterial()
    {
        using var fixture = new SyntheticConfiguration();
        fixture.WriteCertificate(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1), der: true);
        _ = await ConfigurationLoader.LoadAsync(fixture.ConfigPath, CancellationToken.None);
        foreach (var value in new[] { "", "SYNTHETIC-invalid-certificate",
            "-----BEGIN PRIVATE KEY-----\nSYNTHETIC\n-----END PRIVATE KEY-----" })
        {
            File.WriteAllText(fixture.CertificatePath, value);
            await AssertSafeFailure(fixture);
        }
        fixture.WriteCertificate(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        File.AppendAllText(fixture.CertificatePath, "\n-----BEGIN PRIVATE KEY-----\nSYNTHETIC\n-----END PRIVATE KEY-----");
        await AssertSafeFailure(fixture);
        File.Delete(fixture.CertificatePath);
        await AssertSafeFailure(fixture);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{")]
    [InlineData("{}")]
    public async Task RejectsMalformedConfiguration(string json)
    {
        using var fixture = new SyntheticConfiguration();
        File.WriteAllText(fixture.ConfigPath, json);
        await AssertSafeFailure(fixture);
    }

    [Fact]
    public async Task RejectsOversizedOrMissingConfigurationAndPropagatesCancellation()
    {
        using var fixture = new SyntheticConfiguration();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ConfigurationLoader.LoadAsync(fixture.ConfigPath, cancellation.Token));
        File.WriteAllText(fixture.ConfigPath, new string('x', 65537));
        await AssertSafeFailure(fixture);
        File.Delete(fixture.ConfigPath);
        await AssertSafeFailure(fixture);
    }

    private static async Task AssertSafeFailure(SyntheticConfiguration fixture)
    {
        var error = await Assert.ThrowsAsync<ConfigurationException>(() =>
            ConfigurationLoader.LoadAsync(fixture.ConfigPath, CancellationToken.None));
        Assert.Matches("^configuration_[a-z_]+$", error.Message);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain(fixture.DirectoryPath, error.ToString());
        Assert.DoesNotContain(SyntheticConfiguration.SentinelPassword, error.ToString());
        Assert.DoesNotContain(SyntheticConfiguration.SentinelUsername, error.ToString());
    }
}
