using ProtonMailMcp.Tests.Fixtures;
using ProtonMailMcp.Tests.LiveBridge;
using static ProtonMailMcp.Tests.Fixtures.ScriptedImapServer;

namespace ProtonMailMcp.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class LiveAcceptanceSafetyTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("relative.json")]
    [InlineData("/tmp/checkout/config.json")]
    [InlineData("/tmp/checkout/../checkout/config.json")]
    public void ExternalPathsFailClosed(string? path) =>
        Assert.Throws<InvalidOperationException>(() => LiveAcceptance.RequireExternalPath(path, "/tmp/checkout"));

    [Fact]
    public void SymlinkIntoRepositoryIsRejected()
    {
        using var config = new SyntheticConfiguration();
        var link = Path.Combine(config.DirectoryPath, "link");
        Directory.CreateSymbolicLink(link, "/tmp/checkout");
        Assert.Throws<InvalidOperationException>(() => LiveAcceptance.RequireExternalPath(Path.Combine(link, "config.json"), "/tmp/checkout"));
        File.Delete(link);
    }

    [Theory]
    [InlineData("folder")]
    [InlineData("epoch")]
    [InlineData("duplicate")]
    [InlineData("missing-role")]
    [InlineData("no-unread")]
    [InlineData("unknown-flags")]
    [InlineData("deleted-claim")]
    [InlineData("hash")]
    public async Task InvalidManifestNeverConnects(string fault)
    {
        await using var server = new ScriptedImapServer();
        var manifest = new LiveManifest
        {
            Folder = fault == "folder" ? "Other" : "Approved",
            UidValidity = fault == "epoch" ? 0u : 7u,
            PlainUid = 41, HtmlUid = fault == "missing-role" ? 99u : 42u, AttachmentUid = 42,
            PlainTextSha256 = fault == "hash" ? "bad" : new string('a', 64), HtmlTextSha256 = new string('b', 64),
            DeletedState = fault == "deleted-claim" ? "present" : "unsupported",
            Messages = [new LiveFixture { Uid = 41, Flags = fault == "no-unread" ? 1 : 0 },
                new LiveFixture { Uid = fault == "duplicate" ? 41u : 42u, Flags = fault == "unknown-flags" ? 32 : 1 }]
        };
        Assert.Throws<InvalidOperationException>(() => manifest.Validate(server.Settings(folders: ["Approved"])));
        Assert.Equal(0, server.AcceptedConnections);
    }

    [Fact]
    public async Task ValidDedicatedManifestNeedsNoConnectionToValidate()
    {
        await using var server = new ScriptedImapServer();
        new LiveManifest
        {
            Folder = "Approved", UidValidity = 7, PlainUid = 41, HtmlUid = 42, AttachmentUid = 42,
            PlainTextSha256 = new string('a', 64), HtmlTextSha256 = new string('b', 64), DeletedState = "present",
            Messages = [new LiveFixture { Uid = 41 }, new LiveFixture { Uid = 42, Flags = 9 }]
        }.Validate(server.Settings(folders: ["Approved"]));
        Assert.Equal(0, server.AcceptedConnections);
    }

    [Fact]
    public async Task DedicatedFolderAndCompleteKnownIdentitiesAreRequired()
    {
        await using var server = new ScriptedImapServer();
        var manifest = new LiveManifest { Folder = "INBOX", UidValidity = 7 };
        Assert.Throws<InvalidOperationException>(() => manifest.Validate(server.Settings()));
        Assert.Equal(0, server.AcceptedConnections);
    }
}
