using System.Diagnostics;
using System.Text;
using ProtonMailMcp.Tests.Fixtures;
using ProtonMailMcp.Tests.Protocol;
using static ProtonMailMcp.Tests.Fixtures.ScriptedImapServer;

namespace ProtonMailMcp.Tests.Mcp;

[Trait("Category", "Mcp")]
public sealed class KeyringProcessTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DiscoveryNeedsNoBus(bool downloads)
    {
        using var config = new SyntheticConfiguration();
        IsolatedKeyring.Select(config);
        if (downloads) config.EnableDownloads();
        await using var app = await KeyringApplication.StartAsync(config, new Dictionary<string, string> { ["PATH"] = "/usr/bin:/bin" });
        using var tools = await app.DiscoverAsync();
        Assert.Equal(downloads ? 4 : 3, tools.RootElement.GetProperty("result").GetProperty("tools").GetArrayLength());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task KeyringSourceIsLazyAndWrongPinNeverLooksUp(bool wrongPin)
    {
        await using var keyring = await IsolatedKeyring.StartAsync();
        await using var server = new ScriptedImapServer(wrongPin ? new Session([]) : new Session(Login(Logout())));
        using var config = KeyringPasswordTests.Configure(server);
        if (wrongPin) { config.WriteCertificate(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1)); }
        await using var app = await KeyringApplication.StartAsync(config, keyring.Environment);
        using var tools = await app.DiscoverAsync();
        Assert.Equal(3, tools.RootElement.GetProperty("result").GetProperty("tools").GetArrayLength());
        using (var stats = await keyring.StatsAsync()) Assert.Empty(stats.RootElement.GetProperty("calls").EnumerateArray());
        using var result = await app.CallAsync();
        if (wrongPin)
        {
            KeyringApplication.Error(result, "tls_failed");
            using var stats = await keyring.StatsAsync();
            Assert.Empty(stats.RootElement.GetProperty("calls").EnumerateArray());
        }
        else KeyringApplication.Success(result);
        await server.VerifyAsync();
    }

    [Theory]
    [InlineData("missing-bus")]
    [InlineData("remote")]
    [InlineData("native-symbol")]
    [InlineData("service-override")]
    [InlineData("bus-debug")]
    [InlineData("native-debug")]
    public async Task UnavailableNativeTransportOrLibraryIsSanitizedOnlyOnUse(string mode)
    {
        await using var keyring = await IsolatedKeyring.StartAsync();
        await using var server = new ScriptedImapServer(new Session([]));
        using var config = KeyringPasswordTests.Configure(server);
        var environment = new Dictionary<string, string>(keyring.Environment);
        if (mode == "missing-bus") environment.Remove("DBUS_SESSION_BUS_ADDRESS");
        if (mode == "remote") environment["DBUS_SESSION_BUS_ADDRESS"] = "tcp:host=127.0.0.1,port=1";
        if (mode == "bus-debug") environment["G_DBUS_DEBUG"] = "all";
        if (mode == "native-debug") environment["G_MESSAGES_DEBUG"] = "all";
        if (mode == "service-override") environment["SECRET_SERVICE_BUS_NAME"] = "synthetic.override";
        if (mode == "native-symbol")
        {
            // Deliberately incomplete native dependency, only for this child. No compiled shim.
            var native = Path.Combine(config.DirectoryPath, "native");
            Directory.CreateDirectory(native);
            File.CreateSymbolicLink(Path.Combine(native, "libsecret-1.so.0"), "/lib/x86_64-linux-gnu/libc.so.6");
            environment["LD_LIBRARY_PATH"] = native;
        }
        await using var app = await KeyringApplication.StartAsync(config, environment);
        using var tools = await app.DiscoverAsync();
        Assert.Equal(3, tools.RootElement.GetProperty("result").GetProperty("tools").GetArrayLength());
        using var result = await app.CallAsync();
        KeyringApplication.Error(result, "credential_unavailable");
        await server.VerifyAsync();
        Assert.Empty(server.Commands);
    }

    [Fact]
    public async Task PublishedNativeLookupObservesExactByteLimitAndPreservesSpaces()
    {
        foreach (var password in new[] { " surrounding spaces ", new string('x', 4096) })
        {
            await using var keyring = await IsolatedKeyring.StartAsync();
            await keyring.ConfigureAsync("success", Encoding.UTF8.GetBytes(password));
            var wire = password.Contains(' ') ? "\"" + password + "\"" : password;
            var login = Login(Logout());
            login[0] = new Step(System.Text.RegularExpressions.Regex.Escape("LOGIN SYNTHETIC-username-sentinel " + wire),
                "{tag} OK [CAPABILITY IMAP4rev1] authenticated\r\n");
            await using var server = new ScriptedImapServer(new Session(login));
            using var config = KeyringPasswordTests.Configure(server);
            await using var app = await KeyringApplication.StartAsync(config, keyring.Environment);
            using var result = await app.CallAsync();
            KeyringApplication.Success(result);
            await server.VerifyAsync();
        }
    }

    [Fact]
    public async Task ProductionDeadlineCancelsNativeLoadAndFreshCallRecovers()
    {
        await using var keyring = await IsolatedKeyring.StartAsync();
        await keyring.ConfigureAsync("hang-load");
        await using var server = new ScriptedImapServer(new Session([]), new Session(Login(Logout())));
        using var config = KeyringPasswordTests.Configure(server);
        await using var app = await KeyringApplication.StartAsync(config, keyring.Environment);
        var start = Stopwatch.GetTimestamp();
        using var timedOut = await app.CallAsync();
        KeyringApplication.Error(timedOut, "timeout");
        Assert.InRange(Stopwatch.GetElapsedTime(start).TotalSeconds, 28, 36);
        Assert.Empty(server.Commands);
        await keyring.ConfigureAsync("success");
        using var recovered = await app.CallAsync();
        KeyringApplication.Success(recovered);
        await server.VerifyAsync();
    }

    [Fact]
    public async Task EofDrainsNativeLookupUnderExistingDeadline()
    {
        await using var keyring = await IsolatedKeyring.StartAsync();
        await keyring.ConfigureAsync("hang-load");
        await using var server = new ScriptedImapServer(new Session([]));
        using var config = KeyringPasswordTests.Configure(server);
        await using (var app = await KeyringApplication.StartAsync(config, keyring.Environment))
        {
            await app.BeginCallAsync();
            await KeyringPasswordTests.ReachedAsync(keyring, "SearchItems");
            app.CloseInput();
        }
        await server.VerifyAsync();
        Assert.Empty(server.Commands);
    }
}
