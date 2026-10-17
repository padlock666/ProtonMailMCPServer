using ProtonMailMcp.Tests.Fixtures;
using ProtonMailMcp.Tests.LiveBridge;
using static ProtonMailMcp.Tests.Fixtures.ScriptedImapServer;

namespace ProtonMailMcp.Tests.Protocol;

[Trait("Category", "Protocol")]
public sealed class LiveObserverTests
{
    [Fact]
    public async Task ObserverUsesExamineUidFlagsAndSafeLogout()
    {
        await using var server = new ScriptedImapServer(new Session(Login(
            Examine(),
            new Step("UID SEARCH ALL", "* SEARCH 42\r\n{tag} OK searched\r\n"),
            new Step(@"UID FETCH 42 \(UID FLAGS\)", "* 1 FETCH (UID 42 FLAGS (\\Deleted \\Recent synthetic-keyword))\r\n{tag} OK fetched\r\n"),
            Logout())));
        await LiveAcceptance.VerifyState(server.Settings(), new LiveManifest
        {
            Folder = "INBOX", UidValidity = 7,
            Messages = [new LiveFixture { Uid = 42, Flags = 8, Keywords = ["synthetic-keyword"] }]
        }, CancellationToken.None);
        await server.VerifyAsync();
    }

    [Fact]
    public async Task ObserverStateMismatchDisposesWithoutClose()
    {
        await using var server = new ScriptedImapServer(new Session(Login(Examine())));
        await Assert.ThrowsAsync<InvalidOperationException>(() => LiveAcceptance.VerifyState(server.Settings(),
            new LiveManifest { Folder = "INBOX", UidValidity = 8 }, CancellationToken.None));
        await server.VerifyAsync();
    }
}
