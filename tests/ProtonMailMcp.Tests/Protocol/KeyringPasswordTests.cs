using System.Text;
using System.Net.Sockets;
using ProtonMailMcp.Credentials;
using ProtonMailMcp.Mail;
using ProtonMailMcp.Contracts;
using ProtonMailMcp.Tests.Fixtures;
using static ProtonMailMcp.Tests.Fixtures.ScriptedImapServer;

namespace ProtonMailMcp.Tests.Protocol;

[Trait("Category", "Protocol")]
public sealed class KeyringPasswordTests
{
    [Fact]
    public async Task NativePasswordAuthenticatesOnceAndRefreshesNextCall()
    {
        await using var keyring = await IsolatedKeyring.StartAsync();
        await using var server = new ScriptedImapServer(new Session(Login(Logout())),
            new Session([new("LOGIN SYNTHETIC-username-sentinel SYNTHETIC-rotated", "{tag} NO rejected\r\n")]),
            new Session(Login(Logout())));
        using var config = Configure(server);
        await using var app = await KeyringApplication.StartAsync(config, keyring.Environment);
        using var first = await app.CallAsync();
        KeyringApplication.Success(first);
        using (var stats = await keyring.StatsAsync())
        {
            Assert.Equal(1, stats.RootElement.GetProperty("loads").GetInt32());
            Assert.Equal(0, stats.RootElement.GetProperty("active_clients").GetInt32());
        }
        await keyring.ConfigureAsync("success", Encoding.UTF8.GetBytes("SYNTHETIC-rotated"));
        using var rejected = await app.CallAsync();
        KeyringApplication.Error(rejected, "authentication_failed");
        await keyring.ConfigureAsync("success");
        using var recovered = await app.CallAsync();
        KeyringApplication.Success(recovered);
        await server.VerifyAsync();
        Assert.Equal(3, server.Commands.Count(c => c.StartsWith("LOGIN", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("missing-alias", "credential_not_found")]
    [InlineData("missing-item", "credential_not_found")]
    [InlineData("locked-collection", "credential_locked")]
    [InlineData("locked-item", "credential_locked")]
    [InlineData("duplicate", "credential_ambiguous")]
    [InlineData("attributes", "credential_invalid")]
    [InlineData("denied", "credential_unavailable")]
    [InlineData("deleted", "credential_not_found")]
    [InlineData("lock-during-load", "credential_locked")]
    [InlineData("malformed-reply", "credential_invalid")]
    public async Task SelectionErrorsNeverAuthenticateAndFreshCallRecovers(string mode, string code)
    {
        await using var keyring = await IsolatedKeyring.StartAsync();
        await keyring.ConfigureAsync(mode);
        await using var server = new ScriptedImapServer(new Session([]), new Session(Login(Logout())));
        using var config = Configure(server);
        await using var app = await KeyringApplication.StartAsync(config, keyring.Environment);
        using var failed = await app.CallAsync();
        KeyringApplication.Error(failed, code);
        Assert.Empty(server.Commands);
        using (var stats = await keyring.StatsAsync())
        {
            var calls = stats.RootElement.GetProperty("calls").EnumerateArray().Select(c => c.GetString()).ToArray();
            Assert.DoesNotContain(calls, c => c!.Contains("Unlock") || c.Contains("Prompt") || c.Contains("Set") || c.Contains("Create") || c.Contains("Delete"));
            if (mode is "duplicate" or "locked-collection" or "locked-item") Assert.DoesNotContain("GetSecret", calls);
        }
        await keyring.ConfigureAsync("success");
        using var recovered = await app.CallAsync();
        KeyringApplication.Success(recovered);
        await server.VerifyAsync();
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("blank")]
    [InlineData("newline")]
    [InlineData("nul")]
    [InlineData("utf8")]
    [InlineData("oversized")]
    [InlineData("encoding")]
    public async Task InvalidNativeSecretIsSanitizedAndDoesNotAuthenticate(string mode)
    {
        await using var keyring = await IsolatedKeyring.StartAsync();
        byte[] bytes = mode switch
        {
            "empty" => [], "blank" => "  "u8.ToArray(), "newline" => "secret\n"u8.ToArray(),
            "nul" => [97, 0, 98], "utf8" => [0xc0, 0xaf], "oversized" => new byte[4097], _ => "synthetic"u8.ToArray()
        };
        await keyring.ConfigureAsync("success", bytes, mode == "encoding" ? "application/octet-stream" : null);
        await using var server = new ScriptedImapServer(new Session([]));
        using var config = Configure(server);
        await using var app = await KeyringApplication.StartAsync(config, keyring.Environment);
        using var failed = await app.CallAsync();
        KeyringApplication.Error(failed, "credential_invalid");
        await server.VerifyAsync();
        Assert.Empty(server.Commands);
    }

    public static TheoryData<string> OwnerTransitions
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var stage in new[] { "alias", "search", "load", "completion" })
                foreach (var transition in new[] { "loss", "replace", "reacquire", "back" })
                    data.Add("owner-" + stage + "-" + transition);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(OwnerTransitions))]
    public async Task OwnershipTransitionDiscardsLateResultsAndNeverAuthenticates(string mode)
    {
        await using var keyring = await IsolatedKeyring.StartAsync();
        await keyring.ConfigureAsync(mode);
        await using var server = new ScriptedImapServer(new Session([]), new Session(Login(Logout())));
        using var config = Configure(server);
        await using var app = await KeyringApplication.StartAsync(config, keyring.Environment);
        using var failed = await app.CallAsync();
        KeyringApplication.Error(failed, "credential_unavailable");
        Assert.Empty(server.Commands);
        // Deliver the delayed server-side success after the cancelled native callback drained.
        if (mode.StartsWith("owner-load-", StringComparison.Ordinal)) await Task.Delay(1350);
        await keyring.ConfigureAsync("success");
        using var recovered = await app.CallAsync();
        KeyringApplication.Success(recovered);
        await server.VerifyAsync();
    }

    [Theory]
    [InlineData("stall-alias", "ReadAlias")]
    [InlineData("stall-search", "SearchItems")]
    [InlineData("stall-load", "GetSecret")]
    public async Task RemoteCancellationDrainsNativeCallbackAndReleasesMailboxGate(string mode, string reached)
    {
        await using var keyring = await IsolatedKeyring.StartAsync();
        await keyring.ConfigureAsync(mode);
        await using var server = new ScriptedImapServer(new Session([]), new Session(Login(Logout())));
        using var config = Configure(server);
        await using var app = await KeyringApplication.StartAsync(config, keyring.Environment);
        var id = await app.BeginCallAsync();
        await ReachedAsync(keyring, reached);
        await app.CancelAsync(id);
        // MCP permits no response to a cancelled request. Prove remote cancellation by
        // allowing the late service reply, observing no LOGIN, then issuing a fresh call.
        await Task.Delay(1350);
        Assert.Empty(server.Commands);
        await keyring.ConfigureAsync("success");
        using var recovered = await app.CallAsync();
        KeyringApplication.Success(recovered);
        await server.VerifyAsync();
    }

    [Fact]
    public async Task OwnershipChangesAfterCredentialHandoffDoNotRevokeAuthentication()
    {
        await using var keyring = await IsolatedKeyring.StartAsync();
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var proceed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var login = Login(Logout());
        login[0] = login[0] with { Reached = reached, Continue = proceed.Task };
        await using var server = new ScriptedImapServer(new Session(login), new Session(Login(Logout())));
        using var config = Configure(server);
        await using var app = await KeyringApplication.StartAsync(config, keyring.Environment);
        await app.BeginCallAsync();
        await reached.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await keyring.ConfigureAsync("change-now");
        proceed.SetResult();
        using var accepted = await app.ResponseAsync();
        KeyringApplication.Success(accepted);
        using var fresh = await app.CallAsync();
        KeyringApplication.Success(fresh);
        await server.VerifyAsync();
    }

    [Fact]
    public async Task QueuedCancellationAndDeniedFolderNeverStartAnotherLookup()
    {
        await using var keyring = await IsolatedKeyring.StartAsync();
        await keyring.ConfigureAsync("stall-load");
        await using var server = new ScriptedImapServer(new Session([]), new Session(Login(Logout())));
        using var config = Configure(server);
        await using var app = await KeyringApplication.StartAsync(config, keyring.Environment);
        await app.BeginCallAsync("get_message", new { folder = "denied", uidValidity = 7, uid = 42 });
        using var denied = await app.ResponseAsync();
        KeyringApplication.Error(denied, "denied_folder");
        using (var stats = await keyring.StatsAsync()) Assert.Empty(stats.RootElement.GetProperty("calls").EnumerateArray());
        var first = await app.BeginCallAsync();
        await ReachedAsync(keyring, "GetSecret");
        var queued = await app.BeginCallAsync();
        await app.CancelAsync(queued);
        await app.CancelAsync(first);
        await Task.Delay(1350);
        Assert.Equal(1, server.AcceptedConnections);
        using (var stats = await keyring.StatsAsync()) Assert.Equal(1, stats.RootElement.GetProperty("loads").GetInt32());
        Assert.Empty(server.Commands);
        await keyring.ConfigureAsync("success");
        using var recovered = await app.CallAsync();
        KeyringApplication.Success(recovered);
        await server.VerifyAsync();
    }

    [Fact]
    public async Task CancellationReachesNativeBusHandshakeAndReleasesGate()
    {
        await using var server = new ScriptedImapServer(new Session([]), new Session([]));
        using var config = Configure(server);
        var address = Path.Combine(config.DirectoryPath, "stalled-bus");
        using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(address));
        listener.Listen(2);
        await using var app = await KeyringApplication.StartAsync(config, new Dictionary<string, string>
        { ["PATH"] = "/usr/bin:/bin", ["DBUS_SESSION_BUS_ADDRESS"] = "unix:path=" + address });
        for (var i = 0; i < 2; i++)
        {
            var id = await app.BeginCallAsync();
            using var connection = await listener.AcceptAsync().WaitAsync(TimeSpan.FromSeconds(5));
            await app.CancelAsync(id);
        }
        using var tools = await app.DiscoverAsync();
        Assert.Equal(3, tools.RootElement.GetProperty("result").GetProperty("tools").GetArrayLength());
        await server.VerifyAsync();
        Assert.Empty(server.Commands);
        Assert.Equal(2, server.AcceptedConnections);
    }

    [Fact]
    public async Task PasswordLeaseIsDisposedImmediatelyAfterAuthentication()
    {
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var proceed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = new ScriptedImapServer(new Session(Login(Logout() with
            { Reached = reached, Continue = proceed.Task })));
        var source = new TrackingSource();
        using var mailbox = new MailKitMailbox(server.Settings(), passwordSource: source);
        var operation = mailbox.ListFoldersAsync(default);
        await reached.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Throws<ObjectDisposedException>(() => source.Lease.Value);
        proceed.SetResult();
        Assert.Single(await operation);
        await server.VerifyAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AuthenticationFailureAndCancellationDisposeLeaseWithoutRetry(bool cancel)
    {
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var step = new Step("LOGIN SYNTHETIC-username-sentinel SYNTHETIC-password-sentinel",
            cancel ? "" : "{tag} NO rejected\r\n", WaitForDisconnect: cancel, Reached: reached);
        await using var server = new ScriptedImapServer(new Session([step]));
        var source = new TrackingSource();
        using var mailbox = new MailKitMailbox(server.Settings(), passwordSource: source);
        using var caller = new CancellationTokenSource();
        var operation = mailbox.ListFoldersAsync(caller.Token);
        await reached.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (cancel)
        {
            caller.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
        }
        else Assert.Equal(MailboxError.AuthenticationFailed, (await Assert.ThrowsAsync<MailboxException>(() => operation)).Error);
        Assert.Throws<ObjectDisposedException>(() => source.Lease.Value);
        await server.VerifyAsync();
        Assert.Single(server.Commands);
    }

    private sealed class TrackingSource : IPasswordSource
    {
        internal PasswordLease Lease { get; } = new(SyntheticConfiguration.SentinelPassword);
        public Task<PasswordLease> GetAsync(CancellationToken cancellationToken) => Task.FromResult(Lease);
    }

    internal static SyntheticConfiguration Configure(ScriptedImapServer server)
    {
        var config = new SyntheticConfiguration();
        IsolatedKeyring.Select(config);
        config.Values["Port"] = server.Port;
        config.Values["TlsMode"] = "SslOnConnect";
        config.Values["AllowedFolders"] = new[] { "INBOX" };
        File.WriteAllBytes(config.CertificatePath, server.CertificateDer);
        config.Save();
        return config;
    }

    internal static async Task ReachedAsync(IsolatedKeyring keyring, string call)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
        {
            using var stats = await keyring.StatsAsync();
            if (stats.RootElement.GetProperty("calls").EnumerateArray().Any(c => c.GetString() == call)) return;
            await Task.Delay(10, deadline.Token);
        }
    }
}
