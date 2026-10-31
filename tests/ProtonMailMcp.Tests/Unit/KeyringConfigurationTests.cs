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
    public async Task SelectorLoadsWithoutFileOrBusLookup()
    {
        using var config = new SyntheticConfiguration();
        IsolatedKeyring.Select(config);
        var settings = await ConfigurationLoader.LoadAsync(config.ConfigPath, default);
        Assert.Equal("default", settings.PasswordKeyring!.CollectionAlias);
        Assert.Empty(settings.Password);
    }

    [Theory]
    [InlineData("both")]
    [InlineData("both-null")]
    [InlineData("neither")]
    [InlineData("null")]
    [InlineData("alias")]
    [InlineData("uppercase")]
    [InlineData("zero")]
    [InlineData("missing")]
    [InlineData("unknown")]
    [InlineData("wrong-type")]
    [InlineData("duplicate")]
    public async Task InvalidSelectorsFailClosed(string mode)
    {
        using var config = new SyntheticConfiguration();
        IsolatedKeyring.Select(config);
        var id = "44ec5121-59ce-4c39-8b45-551823feb631";
        if (mode == "both") config.Values["PasswordFile"] = config.PasswordPath;
        if (mode == "both-null") config.Values["PasswordFile"] = null;
        if (mode == "neither") config.Values.Remove("PasswordKeyring");
        if (mode == "null") config.Values["PasswordKeyring"] = null;
        if (mode == "alias") config.Values["PasswordKeyring"] = new { CollectionAlias = "Default", ItemId = id };
        if (mode == "uppercase") config.Values["PasswordKeyring"] = new { CollectionAlias = "default", ItemId = id.ToUpperInvariant() };
        if (mode == "zero") config.Values["PasswordKeyring"] = new { CollectionAlias = "default", ItemId = Guid.Empty.ToString() };
        if (mode == "missing") config.Values["PasswordKeyring"] = new { CollectionAlias = "default" };
        if (mode == "unknown") config.Values["PasswordKeyring"] = new { CollectionAlias = "default", ItemId = id, Label = "synthetic" };
        if (mode == "wrong-type") config.Values["PasswordKeyring"] = "synthetic";
        config.Save();
        if (mode == "duplicate")
            File.WriteAllText(config.ConfigPath, File.ReadAllText(config.ConfigPath).Replace("\"CollectionAlias\":\"default\"", "\"CollectionAlias\":\"default\",\"CollectionAlias\":\"default\""));
        var error = await Assert.ThrowsAsync<ConfigurationException>(() => ConfigurationLoader.LoadAsync(config.ConfigPath, default));
        Assert.StartsWith("configuration_", error.Message);
        Assert.Null(error.InnerException);
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
