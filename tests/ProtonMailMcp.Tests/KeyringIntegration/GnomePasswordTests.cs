using ProtonMailMcp.Tests.Fixtures;
using ProtonMailMcp.Tests.Protocol;
using static ProtonMailMcp.Tests.Fixtures.ScriptedImapServer;

namespace ProtonMailMcp.Tests.KeyringIntegration;

[Trait("Category", "KeyringIntegration")]
public sealed class GnomePasswordTests
{
    [KeyringIntegrationFact]
    public async Task UnlockedGnomeNegotiatesEncryptedSessionAndObservesRotation()
    {
        await using var keyring = await IsolatedKeyring.StartAsync(gnome: true);
        await using var server = new ScriptedImapServer(new Session(Login(Logout())),
            new Session([new("LOGIN SYNTHETIC-username-sentinel SYNTHETIC-rotated", "{tag} NO rejected\r\n")]));
        using var config = KeyringPasswordTests.Configure(server);
        await using var app = await KeyringApplication.StartAsync(config, keyring.Environment);
        using var first = await app.CallAsync();
        KeyringApplication.Success(first);
        using (var stats = await keyring.StatsAsync())
        {
            Assert.Equal("dh-ietf1024-sha256-aes128-cbc-pkcs7", stats.RootElement.GetProperty("algorithm").GetString());
            Assert.Equal(1, stats.RootElement.GetProperty("loads").GetInt32());
            Assert.DoesNotContain(stats.RootElement.GetProperty("calls").EnumerateArray(), c => c.GetString()!.StartsWith("FORBIDDEN:", StringComparison.Ordinal));
        }
        await keyring.ConfigureAsync("rotate");
        using var rotated = await app.CallAsync();
        KeyringApplication.Error(rotated, "authentication_failed");
        await server.VerifyAsync();
    }

    [KeyringIntegrationFact]
    public async Task LockedGnomeCollectionFailsWithoutUiOrAuthentication()
    {
        await using var keyring = await IsolatedKeyring.StartAsync(gnome: true);
        await keyring.ConfigureAsync("locked-collection");
        await using var server = new ScriptedImapServer(new Session([]));
        using var config = KeyringPasswordTests.Configure(server);
        await using var app = await KeyringApplication.StartAsync(config, keyring.Environment);
        using var result = await app.CallAsync();
        KeyringApplication.Error(result, "credential_locked");
        using var stats = await keyring.StatsAsync();
        Assert.DoesNotContain(stats.RootElement.GetProperty("calls").EnumerateArray(), c => c.GetString() is "Unlock" or "Prompt" or "GetSecret");
        await server.VerifyAsync();
        Assert.Empty(server.Commands);
    }

    [KeyringIntegrationFact]
    public async Task GnomeDuplicatesAndMissingItemsFailThenExplicitCallerRecovers()
    {
        await using var keyring = await IsolatedKeyring.StartAsync(gnome: true);
        await keyring.ConfigureAsync("duplicate");
        await using var server = new ScriptedImapServer(new Session([]), new Session([]), new Session(Login(Logout())));
        using var config = KeyringPasswordTests.Configure(server);
        await using var app = await KeyringApplication.StartAsync(config, keyring.Environment);
        using var ambiguous = await app.CallAsync();
        KeyringApplication.Error(ambiguous, "credential_ambiguous");
        await keyring.ConfigureAsync("missing-item");
        using var missing = await app.CallAsync();
        KeyringApplication.Error(missing, "credential_not_found");
        Assert.Empty(server.Commands);
        await keyring.ConfigureAsync("success");
        using var recovered = await app.CallAsync();
        KeyringApplication.Success(recovered);
        await server.VerifyAsync();
    }

    [KeyringIntegrationFact]
    public async Task GnomeOwnerLossDuringLookupCancelsAndRestartRecovers()
    {
        await using var keyring = await IsolatedKeyring.StartAsync(gnome: true);
        await keyring.ConfigureAsync("pause");
        await using var server = new ScriptedImapServer(new Session([]), new Session(Login(Logout())));
        using var config = KeyringPasswordTests.Configure(server);
        await using var app = await KeyringApplication.StartAsync(config, keyring.Environment);
        await app.BeginCallAsync();
        // Daemon is stopped; allow the native service-open request to enter its async wait.
        await Task.Delay(250);
        await keyring.ConfigureAsync("owner-loss");
        using var lost = await app.ResponseAsync();
        KeyringApplication.Error(lost, "credential_unavailable");
        Assert.Empty(server.Commands);
        await keyring.ConfigureAsync("restart");
        using var recovered = await app.CallAsync();
        KeyringApplication.Success(recovered);
        await server.VerifyAsync();
    }
}
