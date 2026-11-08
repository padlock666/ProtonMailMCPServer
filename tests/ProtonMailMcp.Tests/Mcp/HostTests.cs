using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ProtonMailMcp.Mail;
using ProtonMailMcp.Tests.Fixtures;

namespace ProtonMailMcp.Tests.Mcp;

[Trait("Category", "Mcp")]
public sealed class HostTests
{
    private static string ApplicationPath => TestApplication.Path;

    [Theory]
    [InlineData("ready")]
    [InlineData("missing")]
    [InlineData("unsafe")]
    [InlineData("locked")]
    public async Task ConcurrentHostsKeepToolsAvailableDespiteDownloadStorageReadiness(string storage)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Tests require Linux.");
        using var fixture = new SyntheticConfiguration();
        var path = fixture.EnableDownloads();
        await using var server = new ScriptedImapServer(
            new ScriptedImapServer.Session(ScriptedImapServer.Login(ScriptedImapServer.Logout())),
            new(ScriptedImapServer.Login(ScriptedImapServer.Logout())),
            new(ScriptedImapServer.Login(ScriptedImapServer.Examine(),
                Protocol.AttachmentDownloadTests.Structure(Protocol.AttachmentDownloadTests.Pdf(4)),
                Protocol.AttachmentDownloadTests.Payload("YQ=="), ScriptedImapServer.Logout())),
            new(ScriptedImapServer.Login(ScriptedImapServer.Examine(),
                Protocol.AttachmentDownloadTests.Structure(Protocol.AttachmentDownloadTests.Pdf(4)),
                Protocol.AttachmentDownloadTests.Payload("Yg=="), ScriptedImapServer.Logout())));
        fixture.Values["Port"] = server.Port;
        fixture.Values["TlsMode"] = "SslOnConnect";
        fixture.Values["AllowedFolders"] = new[] { "INBOX" };
        File.WriteAllBytes(fixture.CertificatePath, server.CertificateDer);
        fixture.Save();
        if (storage == "missing") Directory.Delete(path);
        if (storage == "unsafe") File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite |
            UnixFileMode.UserExecute | UnixFileMode.GroupExecute);
        using var held = storage == "locked" ? await new PrivateDownloadStore(path).AcquireAsync(default) : null;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var stderr = new ConcurrentQueue<string>();
        await using var first = await ConnectAsync(fixture, stderr, timeout.Token);
        await using var second = await ConnectAsync(fixture, stderr, timeout.Token);
        foreach (var client in new[] { first, second })
        {
            Assert.Equal(new[] { "download_attachment", "get_message", "list_folders", "search_messages" },
                (await client.ListToolsAsync(cancellationToken: timeout.Token)).Select(t => t.Name).Order());
            if (storage is "missing" or "unsafe")
            {
                var error = await client.CallToolAsync("download_attachment", DownloadArguments(), cancellationToken: timeout.Token);
                Assert.True(error.IsError);
                Assert.Equal("download_directory_unsafe", Assert.IsType<TextContentBlock>(Assert.Single(error.Content)).Text);
                Assert.Null(error.StructuredContent);
                Assert.Equal(0, server.AcceptedConnections);
            }
        }
        foreach (var client in new[] { first, second })
            Assert.False((await client.CallToolAsync("list_folders", cancellationToken: timeout.Token)).IsError == true);
        Assert.Equal(2, server.AcceptedConnections);
        // Recover storage while both hosts remain alive; startup did not pin its identity.
        if (storage == "missing") Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        if (storage == "unsafe") File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        held?.Dispose();
        foreach (var client in new[] { first, second })
            Assert.False((await client.CallToolAsync("download_attachment", DownloadArguments(), cancellationToken: timeout.Token)).IsError == true);
        using var released = await new PrivateDownloadStore(path).AcquireAsync(timeout.Token);
        await server.VerifyAsync();
        var diagnostics = string.Join('\n', stderr);
        Assert.DoesNotContain("startup:", diagnostics);
        Assert.DoesNotContain(fixture.DirectoryPath, diagnostics);
        Assert.DoesNotContain(SyntheticConfiguration.SentinelPassword, diagnostics);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SeparateHostDownloadsSerializeAndRecheckQuotaAfterPublication(bool quota)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Tests require Linux.");
        using var fixture = new SyntheticConfiguration();
        var path = fixture.EnableDownloads();
        if (quota)
        {
            var filler = Path.Combine(path, "synthetic-quota");
            using (var file = File.Create(filler)) file.SetLength(PrivateDownloadStore.MaxDirectoryBytes - PrivateDownloadStore.MaxFileBytes);
            File.SetUnixFileMode(filler, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sessions = new List<ScriptedImapServer.Session>
        {
            new(ScriptedImapServer.Login(ScriptedImapServer.Examine(),
                Protocol.AttachmentDownloadTests.Structure(Protocol.AttachmentDownloadTests.Pdf(4)),
                Protocol.AttachmentDownloadTests.Payload("YQ=="),
                new("LOGOUT", "* BYE leaving\r\n{tag} OK logout\r\n", Reached: reached, Continue: resume.Task)))
        };
        if (!quota) sessions.Add(new(ScriptedImapServer.Login(ScriptedImapServer.Examine(),
            Protocol.AttachmentDownloadTests.Structure(Protocol.AttachmentDownloadTests.Pdf(4)),
            Protocol.AttachmentDownloadTests.Payload("Yg=="), ScriptedImapServer.Logout())));
        await using var server = new ScriptedImapServer(sessions.ToArray());
        fixture.Values["Port"] = server.Port;
        fixture.Values["TlsMode"] = "SslOnConnect";
        fixture.Values["AllowedFolders"] = new[] { "INBOX" };
        File.WriteAllBytes(fixture.CertificatePath, server.CertificateDer);
        fixture.Save();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var stderr = new ConcurrentQueue<string>();
        await using var first = await ConnectAsync(fixture, stderr, timeout.Token);
        await using var second = await ConnectAsync(fixture, stderr, timeout.Token);
        var firstCall = first.CallToolAsync("download_attachment", DownloadArguments(), cancellationToken: timeout.Token).AsTask();
        try
        {
            await reached.Task.WaitAsync(timeout.Token);
            var secondCall = second.CallToolAsync("download_attachment", DownloadArguments(), cancellationToken: timeout.Token).AsTask();
            await Assert.ThrowsAsync<TimeoutException>(() => secondCall.WaitAsync(TimeSpan.FromMilliseconds(150)));
            Assert.False(server.HasPendingConnection);
            resume.TrySetResult();
            var firstResult = await firstCall;
            var secondResult = await secondCall;
            Assert.False(firstResult.IsError == true);
            if (quota)
            {
                Assert.True(secondResult.IsError);
                Assert.Equal("download_limit_exceeded", Assert.IsType<TextContentBlock>(Assert.Single(secondResult.Content)).Text);
                Assert.Equal(1, server.AcceptedConnections);
            }
            else
            {
                Assert.False(secondResult.IsError == true);
                Assert.Equal(2, server.AcceptedConnections);
            }
            Assert.Equal(2, Directory.GetFiles(path).Length);
            Assert.Empty(Directory.GetFiles(path, "*.part"));
            if (quota) File.Delete(Path.Combine(path, "synthetic-quota"));
            using var released = await new PrivateDownloadStore(path).AcquireAsync(timeout.Token);
        }
        finally { resume.TrySetResult(); }
        await server.VerifyAsync();
    }

    private static Dictionary<string, object?> DownloadArguments() => new()
    { ["folder"] = "INBOX", ["uidValidity"] = 7, ["uid"] = 42, ["attachmentId"] = "1" };

    private static async Task<McpClient> ConnectAsync(SyntheticConfiguration fixture,
        ConcurrentQueue<string> stderr, CancellationToken token) => await McpClient.CreateAsync(
            new StdioClientTransport(new StdioClientTransportOptions
            {
                Command = "dotnet", Arguments = [ApplicationPath, "--config", fixture.ConfigPath],
                EnvironmentVariables = await fixture.KeyringEnvironmentAsync(),
                Name = "SyntheticConcurrentHost", StandardErrorLines = stderr.Enqueue
            }), cancellationToken: token);

    [Fact]
    public async Task OfficialClientInitializesWithoutMailboxServices()
    {
        using var fixture = new SyntheticConfiguration();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var stderr = new ConcurrentQueue<string>();
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Command = "dotnet", Arguments = [ApplicationPath, "--config", fixture.ConfigPath],
            Name = "SyntheticPhase1", StandardErrorLines = line => stderr.Enqueue(line)
        });
        await using (var client = await McpClient.CreateAsync(transport, cancellationToken: timeout.Token))
        {
            Assert.Equal("ProtonMailMcp", client.ServerInfo.Name);
            Assert.NotNull(client.ServerCapabilities.Tools);
            Assert.Equal(3, (await client.ListToolsAsync(cancellationToken: timeout.Token)).Count);
            Assert.Null(client.ServerCapabilities.Prompts);
            Assert.Null(client.ServerCapabilities.Resources);
        }
        Assert.Empty(stderr);
    }

    [Fact]
    public async Task StdoutContainsOnlyProtocolAndEofShutsDownCleanly()
    {
        using var fixture = new SyntheticConfiguration();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var process = Start(["--config", fixture.ConfigPath], await fixture.KeyringEnvironmentAsync());
        try
        {
            var errors = process.StandardError.ReadToEndAsync(timeout.Token);
            // A pinned SDK client is tested above; this separate capture checks every stdout line.
            await process.StandardInput.WriteLineAsync("""
                {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"synthetic","version":"1"}}}
                """);
            await process.StandardInput.FlushAsync(timeout.Token);
            var firstLine = await process.StandardOutput.ReadLineAsync(timeout.Token);
            Assert.NotNull(firstLine);
            using (var response = JsonDocument.Parse(firstLine))
            {
                Assert.Equal("2.0", response.RootElement.GetProperty("jsonrpc").GetString());
                Assert.Equal(1, response.RootElement.GetProperty("id").GetInt32());
                Assert.True(response.RootElement.TryGetProperty("result", out _));
            }
            await process.StandardInput.WriteLineAsync("""
                {"jsonrpc":"2.0","method":"notifications/initialized"}
                """);
            process.StandardInput.Close();
            var remaining = await process.StandardOutput.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            Assert.Equal(0, process.ExitCode);
            Assert.Empty(remaining);
            Assert.Empty(await errors);
        }
        finally { await StopAsync(process); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task ToolTrafficIsProtocolOnlyAndEofDisposesActiveSession(bool inFlight, bool download)
    {
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var steps = new List<ScriptedImapServer.Step>();
        if (download)
        {
            steps.Add(ScriptedImapServer.Examine());
            steps.Add(ProtonMailMcp.Tests.Protocol.AttachmentDownloadTests.Structure(ProtonMailMcp.Tests.Protocol.AttachmentDownloadTests.Pdf(4)));
            steps.Add(ProtonMailMcp.Tests.Protocol.AttachmentDownloadTests.Payload("YQ=="));
        }
        steps.Add(inFlight ? new ScriptedImapServer.Step("LOGOUT", WaitForDisconnect: true, Reached: reached) : ScriptedImapServer.Logout());
        await using var server = new ScriptedImapServer(new ScriptedImapServer.Session(ScriptedImapServer.Login(steps.ToArray())));
        using var fixture = new SyntheticConfiguration();
        fixture.Values["Port"] = server.Port;
        fixture.Values["TlsMode"] = "SslOnConnect";
        fixture.Values["AllowedFolders"] = new[] { "INBOX" };
        File.WriteAllBytes(fixture.CertificatePath, server.CertificateDer);
        var downloadPath = download ? fixture.EnableDownloads() : null;
        fixture.Save();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        using var process = Start(["--config", fixture.ConfigPath], await fixture.KeyringEnvironmentAsync());
        try
        {
            var errors = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.StandardInput.WriteLineAsync("""
                {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"synthetic","version":"1"}}}
                """);
            using var initialized = JsonDocument.Parse((await process.StandardOutput.ReadLineAsync(timeout.Token))!);
            Assert.True(initialized.RootElement.TryGetProperty("result", out _));
            await process.StandardInput.WriteLineAsync("""
                {"jsonrpc":"2.0","method":"notifications/initialized"}
                """);
            await process.StandardInput.WriteLineAsync(download ?
                """{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"download_attachment","arguments":{"folder":"INBOX","uidValidity":7,"uid":42,"attachmentId":"1"}}}""" :
                """{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"list_folders","arguments":{}}}""");
            await process.StandardInput.FlushAsync(timeout.Token);
            if (inFlight) await reached.Task.WaitAsync(timeout.Token);
            else
            {
                using var response = JsonDocument.Parse((await process.StandardOutput.ReadLineAsync(timeout.Token))!);
                Assert.Equal(2, response.RootElement.GetProperty("id").GetInt32());
                var data = response.RootElement.GetProperty("result").GetProperty("structuredContent");
                if (download) Assert.Equal("a", await File.ReadAllTextAsync(data.GetProperty("path").GetString()!));
                else Assert.Equal("INBOX", data.GetProperty("folders")[0].GetProperty("name").GetString());
            }
            process.StandardInput.Close();
            var rest = await process.StandardOutput.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            Assert.Equal(0, process.ExitCode);
            foreach (var line in rest.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                using var json = JsonDocument.Parse(line);
                Assert.Equal("2.0", json.RootElement.GetProperty("jsonrpc").GetString());
            }
            var diagnostics = await errors;
            Assert.DoesNotContain(SyntheticConfiguration.SentinelPassword, diagnostics);
            Assert.DoesNotContain(SyntheticConfiguration.SentinelUsername, diagnostics);
            Assert.DoesNotContain("INBOX", diagnostics);
            Assert.DoesNotContain(fixture.DirectoryPath, diagnostics);
            if (download) Assert.Equal(inFlight ? 0 : 1, Directory.GetFiles(downloadPath!).Length);
            await server.VerifyAsync();
        }
        finally { await StopAsync(process); }
    }

    [Theory]
    [InlineData("missing-arguments")]
    [InlineData("missing-file")]
    [InlineData("invalid-config")]
    [InlineData("writable-config")]
    [InlineData("writable-certificate")]
    [InlineData("ambiguous-folder-policy")]
    public async Task StartupFailureIsSanitizedAndStderrOnly(string scenario)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Tests require Linux.");
        using var fixture = new SyntheticConfiguration();
        var arguments = new[] { "--config", fixture.ConfigPath };
        if (scenario == "missing-arguments") arguments = [];
        if (scenario == "missing-file") File.Delete(fixture.ConfigPath);
        if (scenario == "invalid-config")
        {
            fixture.Values["TlsMode"] = SyntheticConfiguration.SentinelPassword;
            fixture.Save();
        }
        if (scenario == "ambiguous-folder-policy")
        {
            fixture.Values["FolderAccess"] = new { Mode = "AllExcept", ExcludedFolders = Array.Empty<string>() };
            fixture.Save();
        }
        if (scenario == "writable-config")
            File.SetUnixFileMode(fixture.ConfigPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.OtherWrite);
        if (scenario == "writable-certificate")
            File.SetUnixFileMode(fixture.CertificatePath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.OtherWrite);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var process = Start(arguments);
        try
        {
            var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            Assert.Equal(2, process.ExitCode);
            Assert.Empty(await stdout);
            var error = await stderr;
            Assert.Matches("^startup: configuration_[a-z_]+\\n$", error);
            Assert.DoesNotContain(fixture.DirectoryPath, error);
            Assert.DoesNotContain(SyntheticConfiguration.SentinelPassword, error);
            Assert.DoesNotContain(SyntheticConfiguration.SentinelUsername, error);
        }
        finally { await StopAsync(process); }
    }

    private static async Task StopAsync(Process process)
    {
        if (!process.HasExited) process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static Process Start(string[] arguments, IReadOnlyDictionary<string, string?>? environment = null)
    {
        var info = new ProcessStartInfo("dotnet")
        {
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false
        };
        info.ArgumentList.Add(ApplicationPath);
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        if (environment is not null)
        {
            info.Environment.Clear();
            foreach (var pair in environment) info.Environment[pair.Key] = pair.Value;
        }
        // Even hostile log-level overrides must not enable SDK payload diagnostics.
        info.Environment["Logging__LogLevel__Default"] = "Trace";
        return Process.Start(info) ?? throw new InvalidOperationException("Synthetic process failed to start.");
    }
}
