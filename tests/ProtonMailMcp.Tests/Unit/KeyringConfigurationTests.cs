using System.Text;
using ProtonMailMcp.Configuration;
using ProtonMailMcp.Contracts;
using ProtonMailMcp.Credentials;
using ProtonMailMcp.Tests.Fixtures;

namespace ProtonMailMcp.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class KeyringConfigurationTests
{
    [Fact]
    public async Task SelectorLoadsWithoutBusLookup()
    {
        using var config = new SyntheticConfiguration();
        var settings = await ConfigurationLoader.LoadAsync(config.ConfigPath, default);
        Assert.Equal("ProtonMailMcp", settings.Keyring.Application);
        Assert.Equal("BrigePassword", settings.Keyring.Account);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("null")]
    [InlineData("blank")]
    [InlineData("missing-account")]
    [InlineData("missing-application")]
    [InlineData("null-account")]
    [InlineData("null-application")]
    [InlineData("control")]
    [InlineData("oversized")]
    [InlineData("unknown")]
    [InlineData("wrong-type")]
    [InlineData("duplicate")]
    [InlineData("old-file")]
    [InlineData("old-keyring")]
    public async Task InvalidSelectorsFailClosed(string mode)
    {
        using var config = new SyntheticConfiguration();
        if (mode == "missing") config.Values.Remove("Keyring");
        if (mode == "null") config.Values["Keyring"] = null;
        if (mode == "missing-account") config.Values["Keyring"] = new { Application = "app" };
        if (mode == "missing-application") config.Values["Keyring"] = new { Account = "BrigePassword" };
        if (mode == "null-account") config.Values["Keyring"] = new { Application = "app", Account = (string?)null };
        if (mode == "null-application") config.Values["Keyring"] = new { Application = (string?)null, Account = "BrigePassword" };
        if (mode == "blank") config.Values["Keyring"] = new { Application = " ", Account = "BrigePassword" };
        if (mode == "control") config.Values["Keyring"] = new { Application = "app", Account = "a\0b" };
        if (mode == "oversized") config.Values["Keyring"] = new { Application = new string('x', 257), Account = "BrigePassword" };
        if (mode == "unknown") config.Values["Keyring"] = new { Application = "app", Account = "BrigePassword", Label = "ignored" };
        if (mode == "wrong-type") config.Values["Keyring"] = "synthetic";
        if (mode == "old-file") config.Values["PasswordFile"] = config.FixturePath;
        if (mode == "old-keyring") config.Values["PasswordKeyring"] = new { CollectionAlias = "default", ItemId = Guid.NewGuid().ToString() };
        config.Save();
        if (mode == "duplicate")
            File.WriteAllText(config.ConfigPath, File.ReadAllText(config.ConfigPath).Replace("\"Account\":\"BrigePassword\"", "\"Account\":\"BrigePassword\",\"Account\":\"BrigePassword\""));
        var error = await Assert.ThrowsAsync<ConfigurationException>(() => ConfigurationLoader.LoadAsync(config.ConfigPath, default));
        Assert.StartsWith("configuration_", error.Message);
        Assert.Null(error.InnerException);
    }

    [Fact]
    public async Task SelectorLimitsCountUtf8Bytes()
    {
        using var config = new SyntheticConfiguration();
        config.Values["Keyring"] = new { Application = new string('é', 128), Account = "BrigePassword" };
        config.Save();
        _ = await ConfigurationLoader.LoadAsync(config.ConfigPath, default);
        config.Values["Keyring"] = new { Application = new string('é', 129), Account = "BrigePassword" };
        config.Save();
        Assert.Equal("configuration_keyring_invalid", (await Assert.ThrowsAsync<ConfigurationException>(() =>
            ConfigurationLoader.LoadAsync(config.ConfigPath, default))).Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("secret\n")]
    [InlineData("secret\r")]
    [InlineData("a\0b")]
    [InlineData("a\tb")]
    public void SecretValidationClearsOwnedBytes(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        Assert.Equal(MailboxError.CredentialInvalid, Assert.Throws<MailboxException>(() => LibsecretPasswordSource.Decode(bytes)).Error);
        Assert.All(bytes, b => Assert.Equal(0, b));
    }

    [Fact]
    public void SecretBoundariesAndStrictUtf8()
    {
        var bytes = Encoding.UTF8.GetBytes(" surrounding spaces ");
        Assert.Equal(" surrounding spaces ", LibsecretPasswordSource.Decode(bytes));
        Assert.All(bytes, b => Assert.Equal(0, b));
        Assert.Equal(new string('x', 4096), LibsecretPasswordSource.Decode(Encoding.UTF8.GetBytes(new string('x', 4096))));
        foreach (var invalid in new[] { new byte[] { 0xc0, 0xaf }, new byte[4097] })
        {
            Assert.Throws<MailboxException>(() => LibsecretPasswordSource.Decode(invalid));
            Assert.All(invalid, b => Assert.Equal(0, b));
        }
        using var lease = new PasswordLease("synthetic");
        Assert.DoesNotContain("synthetic", lease.ToString()!);
        lease.Dispose();
        Assert.Throws<ObjectDisposedException>(() => lease.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("autolaunch:")]
    [InlineData("tcp:host=localhost,port=1")]
    [InlineData("unix:path=/tmp/a;tcp:host=example")]
    [InlineData("unix:tmpdir=/tmp")]
    [InlineData("unix:path=/tmp/a,abstract=b")]
    public void RejectsNonlocalOrAutomaticTransport(string? address) =>
        Assert.Equal(MailboxError.CredentialUnavailable, Assert.Throws<MailboxException>(() => LibsecretPasswordSource.ValidateTransport(address)).Error);
}
