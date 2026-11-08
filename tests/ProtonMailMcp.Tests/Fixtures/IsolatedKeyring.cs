using System.Diagnostics;
using System.Text.Json;
using ProtonMailMcp.Configuration;

namespace ProtonMailMcp.Tests.Fixtures;

internal sealed class IsolatedKeyring : IAsyncDisposable
{
    private readonly Process process;
    private readonly Task<string> errors;
    internal Dictionary<string, string> Environment { get; }
    private IsolatedKeyring(Process process, Dictionary<string, string> environment)
    {
        this.process = process;
        Environment = environment;
        errors = process.StandardError.ReadToEndAsync();
    }

    internal static async Task<IsolatedKeyring> StartAsync(bool gnome = false)
    {
        if (gnome && System.Environment.GetEnvironmentVariable(KeyringIntegrationAuthorization.Gate) != KeyringIntegrationAuthorization.Value)
            throw new InvalidOperationException("isolated_keyring_authorization_required");
        var info = new ProcessStartInfo("/usr/bin/python3")
        { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        info.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "native", "keyring_fixture.py"));
        info.ArgumentList.Add(gnome ? "gnome" : "fake");
        info.Environment.Clear();
        info.Environment["PATH"] = "/usr/bin:/bin";
        info.Environment["LANG"] = "C.UTF-8";
        if (gnome) info.Environment[KeyringIntegrationAuthorization.Gate] = KeyringIntegrationAuthorization.Value;
        var process = Process.Start(info)!;
        try
        {
            var line = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.NotNull(line);
            using var ready = JsonDocument.Parse(line);
            return new IsolatedKeyring(process, ready.RootElement.GetProperty("environment").Deserialize<Dictionary<string, string>>()!);
        }
        catch
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            process.Dispose();
            throw;
        }
    }

    internal async Task ConfigureAsync(string mode, byte[]? bytes = null, string? contentType = null)
    {
        var data = new Dictionary<string, object?> { ["mode"] = mode };
        if (bytes is not null) data["bytes"] = bytes.Select(b => (int)b).ToArray();
        if (contentType is not null) data["contentType"] = contentType;
        using var result = await CommandAsync(data);
        Assert.True(result.RootElement.GetProperty("configured").GetBoolean());
    }

    internal async Task<JsonDocument> StatsAsync()
    {
        var result = await CommandAsync(new { mode = "stats" });
        Assert.DoesNotContain(result.RootElement.GetProperty("calls").EnumerateArray(),
            c => c.GetString()!.StartsWith("FORBIDDEN:", StringComparison.Ordinal));
        return result;
    }
    private async Task<JsonDocument> CommandAsync(object value)
    {
        await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(value));
        await process.StandardInput.FlushAsync();
        return JsonDocument.Parse((await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)))!);
    }

    internal static void Select(SyntheticConfiguration config)
    {
        config.Values["Keyring"] = new { Application = "ProtonMailMcp", Account = "BrigePassword" };
        config.Save();
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await process.StandardInput.WriteLineAsync("{\"mode\":\"stop\"}");
            await process.StandardInput.FlushAsync();
            var line = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, process.ExitCode);
            Assert.Empty(await errors);
            using var result = JsonDocument.Parse(line!);
            Assert.True(result.RootElement.GetProperty("cleaned").GetBoolean());
            Assert.True(result.RootElement.GetProperty("reaped").GetBoolean());
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            process.Dispose();
        }
    }
}

// Raw stdio captures every output line and supports explicit remote cancellation.
internal sealed class KeyringApplication : IAsyncDisposable
{
    private readonly Process process;
    private readonly Task<string> errors;
    private int id = 1;
    private KeyringApplication(Process process)
    {
        this.process = process;
        errors = process.StandardError.ReadToEndAsync();
    }

    internal static async Task<KeyringApplication> StartAsync(SyntheticConfiguration config,
        IReadOnlyDictionary<string, string> environment)
    {
        var info = new ProcessStartInfo("/usr/bin/dotnet")
        { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { TestApplication.Path, "--config", config.ConfigPath }) info.ArgumentList.Add(arg);
        info.Environment.Clear();
        foreach (var pair in environment) info.Environment[pair.Key] = pair.Value;
        Assert.DoesNotContain(info.ArgumentList, argument => argument.Contains(SyntheticConfiguration.SentinelPassword, StringComparison.Ordinal));
        Assert.DoesNotContain(info.Environment.Values, value => value?.Contains(SyntheticConfiguration.SentinelPassword, StringComparison.Ordinal) == true);
        var app = new KeyringApplication(Process.Start(info)!);
        try
        {
            await app.SendAsync(new { jsonrpc = "2.0", id = 1, method = "initialize", @params = new
            { protocolVersion = "2025-11-25", capabilities = new { }, clientInfo = new { name = "synthetic", version = "1" } } });
            using var initialized = await app.ResponseAsync();
            Assert.True(initialized.RootElement.TryGetProperty("result", out _));
            await app.SendAsync(new { jsonrpc = "2.0", method = "notifications/initialized" });
            return app;
        }
        catch { await app.DisposeAsync(); throw; }
    }

    internal async Task<int> BeginCallAsync(string name = "list_folders", object? arguments = null)
    {
        var requestId = ++id;
        await SendAsync(new { jsonrpc = "2.0", id = requestId, method = "tools/call", @params = new { name, arguments = arguments ?? new { } } });
        return requestId;
    }
    internal async Task<JsonDocument> CallAsync()
    {
        var requestId = await BeginCallAsync();
        while (true)
        {
            var response = await ResponseAsync();
            var responseId = response.RootElement.GetProperty("id").GetInt32();
            if (responseId == requestId) return response;
            // A cancelled request may still receive a reply. Do not attribute that reply
            // to a later operation when verifying cancellation and fresh-call recovery.
            Assert.True(responseId < requestId);
            response.Dispose();
        }
    }
    internal async Task<JsonDocument> DiscoverAsync()
    {
        await SendAsync(new { jsonrpc = "2.0", id = ++id, method = "tools/list" });
        return await ResponseAsync();
    }
    internal Task CancelAsync(int requestId) => SendAsync(new
    { jsonrpc = "2.0", method = "notifications/cancelled", @params = new { requestId } });
    internal async Task<JsonDocument> ResponseAsync() => JsonDocument.Parse(
        (await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(40)))!);
    private async Task SendAsync(object value)
    {
        await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(value));
        await process.StandardInput.FlushAsync();
    }

    internal static void Success(JsonDocument response) => Assert.False(
        response.RootElement.GetProperty("result").TryGetProperty("isError", out var error) && error.GetBoolean());
    internal static void Error(JsonDocument response, string code)
    {
        var result = response.RootElement.GetProperty("result");
        Assert.True(result.GetProperty("isError").GetBoolean());
        Assert.Equal(code, result.GetProperty("content")[0].GetProperty("text").GetString());
        Assert.DoesNotContain(SyntheticConfiguration.SentinelPassword, response.RootElement.GetRawText());
    }

    internal void CloseInput() => process.StandardInput.Close();
    public async ValueTask DisposeAsync()
    {
        try
        {
            process.StandardInput.Close();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(40));
            Assert.Equal(0, process.ExitCode);
            var rest = await process.StandardOutput.ReadToEndAsync();
            foreach (var line in rest.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                using var json = JsonDocument.Parse(line);
                Assert.Equal("2.0", json.RootElement.GetProperty("jsonrpc").GetString());
                Assert.DoesNotContain(SyntheticConfiguration.SentinelPassword, line);
            }
            var diagnostics = await errors;

            Assert.DoesNotContain(SyntheticConfiguration.SentinelPassword, diagnostics);
            Assert.DoesNotContain(SyntheticConfiguration.SentinelUsername, diagnostics);
            Assert.DoesNotContain("ProtonMailMcp", diagnostics);
            foreach (var line in diagnostics.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                Assert.Matches(@"\Atool: (list_folders|search_messages|get_message|download_attachment) correlation=[a-f0-9]{32} duration_ms=[0-9]+ outcome=[a-z_]+\z", line);
            Assert.DoesNotContain("org.freedesktop", diagnostics);
            Assert.DoesNotContain("unix:", diagnostics);
            Assert.DoesNotContain("SYNTHETIC-native-error", diagnostics);
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            process.Dispose();
        }
    }
}
