using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using ModelContextProtocol.Client;
using ProtonMailMcp.Tests.Fixtures;

namespace ProtonMailMcp.Tests.Mcp;

[Trait("Category", "Mcp")]
public sealed class HostTests
{
    private static string ApplicationPath => Path.Combine(AppContext.BaseDirectory, "ProtonMailMcp.dll");

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
        using var process = Start(["--config", fixture.ConfigPath]);
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
        finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ToolTrafficIsProtocolOnlyAndEofDisposesActiveSession(bool inFlight)
    {
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = new ScriptedImapServer(new ScriptedImapServer.Session(ScriptedImapServer.Login(
            inFlight ? new ScriptedImapServer.Step("LOGOUT", WaitForDisconnect: true, Reached: reached) : ScriptedImapServer.Logout())));
        using var fixture = new SyntheticConfiguration();
        fixture.Values["Port"] = server.Port;
        fixture.Values["TlsMode"] = "SslOnConnect";
        fixture.Values["AllowedFolders"] = new[] { "INBOX" };
        File.WriteAllBytes(fixture.CertificatePath, server.CertificateDer);
        fixture.Save();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        using var process = Start(["--config", fixture.ConfigPath]);
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
            await process.StandardInput.WriteLineAsync("""
                {"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"list_folders","arguments":{}}}
                """);
            await process.StandardInput.FlushAsync(timeout.Token);
            if (inFlight) await reached.Task.WaitAsync(timeout.Token);
            else
            {
                using var response = JsonDocument.Parse((await process.StandardOutput.ReadLineAsync(timeout.Token))!);
                Assert.Equal(2, response.RootElement.GetProperty("id").GetInt32());
                Assert.Equal("INBOX", response.RootElement.GetProperty("result").GetProperty("structuredContent").GetProperty("folders")[0].GetProperty("name").GetString());
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
            await server.VerifyAsync();
        }
        finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
    }

    [Theory]
    [InlineData("missing-arguments")]
    [InlineData("missing-file")]
    [InlineData("invalid-config")]
    public async Task StartupFailureIsSanitizedAndStderrOnly(string scenario)
    {
        using var fixture = new SyntheticConfiguration();
        var arguments = new[] { "--config", fixture.ConfigPath };
        if (scenario == "missing-arguments") arguments = [];
        if (scenario == "missing-file") File.Delete(fixture.ConfigPath);
        if (scenario == "invalid-config")
        {
            fixture.Values["TlsMode"] = SyntheticConfiguration.SentinelPassword;
            fixture.Save();
        }
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
        finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
    }

    private static Process Start(string[] arguments)
    {
        var info = new ProcessStartInfo("dotnet")
        {
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false
        };
        info.ArgumentList.Add(ApplicationPath);
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        // Even hostile log-level overrides must not enable SDK payload diagnostics.
        info.Environment["Logging__LogLevel__Default"] = "Trace";
        return Process.Start(info) ?? throw new InvalidOperationException("Synthetic process failed to start.");
    }
}
