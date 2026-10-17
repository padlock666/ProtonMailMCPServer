using System.Collections.Concurrent;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ProtonMailMcp.Tests.Fixtures;
using static ProtonMailMcp.Tests.Fixtures.ScriptedImapServer;

namespace ProtonMailMcp.Tests.Mcp;

[Trait("Category", "Mcp")]
public sealed class ToolTests
{
    private const string Plain = "(\"TEXT\" \"PLAIN\" (\"CHARSET\" \"UTF-8\") NIL NIL \"7BIT\" 5 1 NIL NIL NIL NIL)";
    private const string Fetch = @"UID FETCH 42 \(UID FLAGS INTERNALDATE RFC822.SIZE ENVELOPE BODYSTRUCTURE\)";
    private static Step Summary(string structure = Plain) => new(Fetch,
        "* 1 FETCH (UID 42 FLAGS (\\Deleted) RFC822.SIZE 100 ENVELOPE (NIL \"SYNTHETIC-subject-sentinel\" NIL NIL NIL NIL NIL NIL NIL NIL) BODYSTRUCTURE " + structure + ")\r\n{tag} OK fetched\r\n");
    private static Step Body(string value, string section = "TEXT") => new(@"UID FETCH 42 \(BODY\.PEEK\[" + section + @"\]\)",
        $"* 1 FETCH (UID 42 BODY[{section}] {{{value.Length}}}\r\n{value})\r\n{{tag}} OK body\r\n");
    private static Dictionary<string, object?> Reference() => new() { ["folder"] = "INBOX", ["uidValidity"] = 7, ["uid"] = 42 };
    private static JsonElement Data(CallToolResult result)
    {
        Assert.False(result.IsError == true);
        var structured = Assert.IsType<JsonElement>(result.StructuredContent);
        Assert.Equal(structured.GetRawText(), Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
        return structured;
    }
    private static void Error(CallToolResult result, string code)
    {
        Assert.True(result.IsError);
        Assert.Null(result.StructuredContent);
        Assert.Equal(code, Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
    }

    [Fact]
    public async Task DiscoveryFreezesOnlyApprovedSchemasAndAnnotationsWithoutConnecting()
    {
        await using var server = new ScriptedImapServer();
        await using var host = await ClientHost.StartAsync(server);
        var tools = await host.Client.ListToolsAsync();
        Assert.Equal(new[] { "get_message", "list_folders", "search_messages" }, tools.Select(t => t.Name).Order());
        Assert.Null(host.Client.ServerCapabilities.Resources);
        Assert.Null(host.Client.ServerCapabilities.Prompts);
        foreach (var tool in tools)
        {
            var protocol = tool.ProtocolTool;
            Assert.True(protocol.Annotations!.ReadOnlyHint);
            Assert.False(protocol.Annotations.DestructiveHint);
            Assert.Contains("untrusted", protocol.Description);
            Assert.False(protocol.InputSchema.GetProperty("additionalProperties").GetBoolean());
            Assert.NotNull(protocol.OutputSchema);
        }
        var search = tools.Single(t => t.Name == "search_messages").ProtocolTool;
        Assert.Equal(new[] { "folder" }, search.InputSchema.GetProperty("required").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(new[] { "folder", "limit", "receivedEnd", "receivedStart", "recipient", "sender", "subject", "unread" },
            search.InputSchema.GetProperty("properties").EnumerateObject().Select(x => x.Name).Order());
        Assert.Equal(20, search.InputSchema.GetProperty("properties").GetProperty("limit").GetProperty("default").GetInt32());
        var get = tools.Single(t => t.Name == "get_message").ProtocolTool;
        Assert.Equal(new[] { "folder", "uid", "uidValidity" }, get.InputSchema.GetProperty("required").EnumerateArray().Select(x => x.GetString()).Order());
        Assert.Equal(new[] { "attachments", "attachmentsTruncated", "contentStatus", "summary", "text", "truncated" },
            get.OutputSchema!.Value.GetProperty("properties").EnumerateObject().Select(x => x.Name).Order());
        Assert.Equal(new[] { "available", "missing", "unsupported", "oversized", "malformed" },
            get.OutputSchema.Value.GetProperty("properties").GetProperty("contentStatus").GetProperty("enum").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(0, server.AcceptedConnections);
    }

    [Fact]
    public async Task AllToolsUseRealAdapterAndReturnStructuredMetadataAndFilteredSearch()
    {
        const string attachment = "(\"APPLICATION\" \"OCTET-STREAM\" (\"NAME\" \"../synthetic.bin\") NIL NIL \"BASE64\" 900 NIL (\"ATTACHMENT\" (\"FILENAME\" \"../synthetic.bin\")) NIL NIL)";
        await using var server = new ScriptedImapServer(
            new Session(Login(Logout())),
            new Session(Login(Examine(),
                new("UID SEARCH FROM sender OR TO recipient CC recipient SUBJECT subject SINCE 1-Oct-2026 BEFORE 18-Oct-2026 UNSEEN", "* SEARCH 41 42\r\n{tag} OK search\r\n"),
                new(@"UID FETCH 42 \(UID FLAGS INTERNALDATE RFC822.SIZE ENVELOPE\)", "* 1 FETCH (UID 42 FLAGS () RFC822.SIZE 100 ENVELOPE (NIL NIL NIL NIL NIL NIL NIL NIL NIL NIL))\r\n{tag} OK fetched\r\n"), Logout())),
            new Session(Login(Examine(), Summary("(" + Plain + attachment + "\"MIXED\")"), Body("hello", "1"), Logout())));
        await using var host = await ClientHost.StartAsync(server);
        var folders = Data(await host.Client.CallToolAsync("list_folders"));
        Assert.Equal("INBOX", Assert.Single(folders.GetProperty("folders").EnumerateArray()).GetProperty("name").GetString());
        var search = Data(await host.Client.CallToolAsync("search_messages", new Dictionary<string, object?>
        {
            ["folder"] = "INBOX", ["sender"] = "sender", ["recipient"] = "recipient", ["subject"] = "subject",
            ["receivedStart"] = "2026-10-01", ["receivedEnd"] = "2026-10-18", ["unread"] = true, ["limit"] = 1
        }));
        Assert.True(search.GetProperty("truncated").GetBoolean());
        var summary = Assert.Single(search.GetProperty("messages").EnumerateArray());
        Assert.Equal(new[] { "cc", "from", "internalDate", "reference", "seen", "senderDate", "size", "subject", "to" }, summary.EnumerateObject().Select(x => x.Name).Order());
        Assert.Equal(JsonValueKind.Null, summary.GetProperty("subject").ValueKind);
        var reference = summary.GetProperty("reference");
        Assert.Equal(7u, reference.GetProperty("uidValidity").GetUInt32());
        Assert.Equal(42u, reference.GetProperty("uid").GetUInt32());
        var message = Data(await host.Client.CallToolAsync("get_message", Reference()));
        Assert.Equal("hello", message.GetProperty("text").GetString());
        Assert.Equal("available", message.GetProperty("contentStatus").GetString());
        Assert.False(message.GetProperty("truncated").GetBoolean());
        Assert.False(message.GetProperty("attachmentsTruncated").GetBoolean());
        var metadata = Assert.Single(message.GetProperty("attachments").EnumerateArray());
        Assert.Equal("../synthetic.bin", metadata.GetProperty("filename").GetString());
        Assert.Equal("APPLICATION/OCTET-STREAM", metadata.GetProperty("mimeType").GetString());
        Assert.Equal(900, metadata.GetProperty("size").GetInt32());
        await server.VerifyAsync();
    }

    [Theory]
    [InlineData(null, "ALL")]
    [InlineData(false, "SEEN")]
    [InlineData(true, "UNSEEN")]
    public async Task OptionalFiltersAndEmptyResults(bool? unread, string query)
    {
        await using var server = new ScriptedImapServer(new Session(Login(Examine(), new("UID SEARCH " + query, "* SEARCH\r\n{tag} OK empty\r\n"), Logout())));
        await using var host = await ClientHost.StartAsync(server);
        var args = new Dictionary<string, object?> { ["folder"] = "inbox" };
        if (unread.HasValue) args["unread"] = unread;
        var data = Data(await host.Client.CallToolAsync("search_messages", args));
        Assert.Empty(data.GetProperty("messages").EnumerateArray());
        Assert.False(data.GetProperty("truncated").GetBoolean());
        await server.VerifyAsync();
    }

    [Theory]
    [InlineData("list_folders", "{\"extra\":\"SYNTHETIC-input-sentinel\"}", "invalid_input")]
    [InlineData("search_messages", "{}", "invalid_input")]
    [InlineData("search_messages", "{\"folder\":null}", "invalid_input")]
    [InlineData("search_messages", "{\"folder\":\"private\"}", "denied_folder")]
    [InlineData("search_messages", "{\"folder\":\"INBOX\",\"limit\":0}", "invalid_input")]
    [InlineData("search_messages", "{\"folder\":\"INBOX\",\"limit\":51}", "invalid_input")]
    [InlineData("search_messages", "{\"folder\":\"INBOX\",\"limit\":\"20\"}", "invalid_input")]
    [InlineData("search_messages", "{\"Folder\":\"INBOX\"}", "invalid_input")]
    [InlineData("search_messages", "{\"folder\":\"INBOX\",\"unread\":\"false\"}", "invalid_input")]
    [InlineData("search_messages", "{\"folder\":\"INBOX\",\"receivedStart\":\"bad\"}", "invalid_input")]
    [InlineData("search_messages", "{\"folder\":\"INBOX\",\"receivedStart\":\"2026-10-18\",\"receivedEnd\":\"2026-10-17\"}", "invalid_input")]
    [InlineData("search_messages", "{\"folder\":\"INBOX\",\"subject\":\" \"}", "invalid_input")]
    [InlineData("get_message", "{\"folder\":\"INBOX/child\",\"uidValidity\":7,\"uid\":42}", "denied_folder")]
    [InlineData("get_message", "{\"folder\":\"INBOX\",\"uid\":42}", "invalid_input")]
    [InlineData("get_message", "{\"folder\":\"INBOX\",\"uidValidity\":7,\"uid\":0}", "invalid_input")]
    [InlineData("get_message", "{\"folder\":\"INBOX\",\"uidValidity\":7,\"uid\":\"42\"}", "invalid_input")]
    [InlineData("get_message", "{\"folder\":\"INBOX\",\"uidValidity\":7,\"uid\":-1}", "invalid_input")]
    [InlineData("get_message", "{\"folder\":\"INBOX\",\"uidValidity\":7,\"uid\":4294967296}", "invalid_input")]
    public async Task RejectsInputsBeforeConnecting(string tool, string json, string code)
    {
        await using var server = new ScriptedImapServer();
        await using var host = await ClientHost.StartAsync(server);
        var args = JsonSerializer.Deserialize<Dictionary<string, object?>>(json)!;
        Error(await host.Client.CallToolAsync(tool, args), code);
        Assert.Equal(0, server.AcceptedConnections);
    }

    [Theory]
    [InlineData("stale", "stale_reference")]
    [InlineData("missing", "message_not_found")]
    [InlineData("auth", "authentication_failed")]
    [InlineData("tls", "tls_failed")]
    [InlineData("broken", "bridge_unavailable")]
    [InlineData("protocol", "protocol_failure")]
    [InlineData("folder", "folder_unavailable")]
    public async Task RuntimeErrorsAreSanitizedAndNextCallRecovers(string mode, string code)
    {
        Step[] steps = mode switch
        {
            "auth" => [new("LOGIN SYNTHETIC-username-sentinel SYNTHETIC-password-sentinel", "{tag} NO SYNTHETIC-sensitive-error\r\n")],
            "tls" => [],
            "stale" => Login(Examine(validity: 8)),
            "broken" => Login(Examine(), new(Fetch, EndConnection: true)),
            "protocol" => Login(Examine(), new(Fetch, "{tag} NO SYNTHETIC-sensitive-error\r\n")),
            _ => Login(Examine(), new(Fetch, "{tag} OK missing\r\n"))
        };
        if (mode == "folder")
        {
            steps = Login();
            steps[2] = steps[2] with { Response = "* LIST (\\Noselect) \"/\" \"INBOX\"\r\n{tag} OK list\r\n" };
        }
        await using var server = new ScriptedImapServer(new Session(steps), new Session(Login(Logout())));
        await using var host = await ClientHost.StartAsync(server, wrongPin: mode == "tls");
        Error(await host.Client.CallToolAsync("get_message", Reference()), code);
        if (mode == "tls")
        {
            // Same wrong-pin client cannot recover; use a separately configured process.
            await using var recovered = await ClientHost.StartAsync(server);
            Data(await recovered.Client.CallToolAsync("list_folders"));
        }
        else Data(await host.Client.CallToolAsync("list_folders"));
        await server.VerifyAsync();
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("unsupported")]
    [InlineData("oversized")]
    [InlineData("malformed")]
    [InlineData("truncated")]
    public async Task ContentStatusesAreSuccessfulMetadataResults(string mode)
    {
        var steps = new List<Step> { Examine() };
        steps.Add(Summary(mode switch
        {
            "missing" => "NIL",
            "unsupported" => Plain.Replace("TEXT", "APPLICATION").Replace("PLAIN", "OCTET-STREAM"),
            "oversized" => Plain.Replace("5 1", "1048577 1"),
            "malformed" => Plain.Replace("7BIT", "BASE64"),
            _ => Plain.Replace("5 1", "32001 1")
        }));
        if (mode == "malformed") steps.Add(Body("!bad!"));
        if (mode == "truncated") steps.Add(Body(new string('x', 32001)));
        steps.Add(Logout());
        await using var server = new ScriptedImapServer(new Session(Login(steps.ToArray())));
        await using var host = await ClientHost.StartAsync(server);
        var data = Data(await host.Client.CallToolAsync("get_message", Reference()));
        Assert.Equal(mode == "truncated" ? "available" : mode, data.GetProperty("contentStatus").GetString());
        Assert.Equal(mode == "truncated", data.GetProperty("truncated").GetBoolean());
        if (mode == "truncated") Assert.Equal(32000, data.GetProperty("text").GetString()!.Length);
        else Assert.Equal(JsonValueKind.Null, data.GetProperty("text").ValueKind);
        await server.VerifyAsync();
    }

    [Fact]
    public async Task AttachmentMetadataTruncationIsExplicitWithoutPayloadFetch()
    {
        const string attachment = "(\"APPLICATION\" \"OCTET-STREAM\" NIL NIL NIL \"BASE64\" 100 NIL (\"ATTACHMENT\" (\"FILENAME\" \"synthetic.bin\")) NIL NIL)";
        var structure = "(" + Plain + string.Concat(Enumerable.Repeat(attachment, 101)) + "\"MIXED\")";
        await using var server = new ScriptedImapServer(new Session(Login(Examine(), Summary(structure), Body("hello", "1"), Logout())));
        await using var host = await ClientHost.StartAsync(server);
        var data = Data(await host.Client.CallToolAsync("get_message", Reference()));
        Assert.Equal(100, data.GetProperty("attachments").GetArrayLength());
        Assert.True(data.GetProperty("attachmentsTruncated").GetBoolean());
        await server.VerifyAsync();
    }

    [Fact]
    public async Task CancellationNotificationDisposesInFlightSessionAndReleasesGate()
    {
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = new ScriptedImapServer(
            new Session(Login(Examine(), Summary(), new(@"UID FETCH 42 \(BODY\.PEEK\[TEXT\]\)", "* 1 FETCH (UID 42 BODY[TEXT] {5}\r\nhe", WaitForDisconnect: true, Reached: reached))),
            new Session(Login(Logout())));
        await using var host = await ClientHost.StartAsync(server);
        // Explicit ID avoids depending on the client's internally assigned request IDs.
        // SendMessageAsync both transmits the cancellation and cancels the SDK pending await.
        var call = host.Client.SendRequestAsync(new JsonRpcRequest
        {
            Id = new RequestId(99), Method = "tools/call",
            Params = System.Text.Json.Nodes.JsonNode.Parse(
                "{\"name\":\"get_message\",\"arguments\":{\"folder\":\"INBOX\",\"uidValidity\":7,\"uid\":42}}")
        });
        await reached.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await host.Client.SendMessageAsync(new JsonRpcNotification
        {
            Method = "notifications/cancelled",
            Params = System.Text.Json.Nodes.JsonNode.Parse("{\"requestId\":99}")
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
        Data(await host.Client.CallToolAsync("list_folders").AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
        await server.VerifyAsync();
    }

    [Fact]
    public async Task RealOperationDeadlineReturnsSafeTimeoutAndRecovers()
    {
        await using var server = new ScriptedImapServer(
            new Session(Login(Examine(), new(Fetch, WaitForDisconnect: true))),
            new Session(Login(Logout())));
        await using var host = await ClientHost.StartAsync(server);
        Error(await host.Client.CallToolAsync("get_message", Reference()), "timeout");
        Data(await host.Client.CallToolAsync("list_folders"));
        await server.VerifyAsync();
    }

    [Fact]
    public async Task UnavailableSyntheticEndpointReturnsBridgeUnavailable()
    {
        var server = new ScriptedImapServer();
        await using var host = await ClientHost.StartAsync(server);
        await server.DisposeAsync();
        Error(await host.Client.CallToolAsync("list_folders"), "bridge_unavailable");
    }

    private sealed class ClientHost : IAsyncDisposable
    {
        private readonly SyntheticConfiguration config = new();
        private readonly ConcurrentQueue<string> stderr = new();
        public McpClient Client { get; private set; } = null!;
        public static async Task<ClientHost> StartAsync(ScriptedImapServer server, bool wrongPin = false)
        {
            var host = new ClientHost();
            host.config.Values["Port"] = server.Port;
            host.config.Values["TlsMode"] = "SslOnConnect";
            host.config.Values["AllowedFolders"] = new[] { "INBOX" };
            if (!wrongPin) File.WriteAllBytes(host.config.CertificatePath, server.CertificateDer);
            host.config.Save();
            host.Client = await McpClient.CreateAsync(new StdioClientTransport(new StdioClientTransportOptions
            {
                Command = "dotnet", Arguments = [Path.Combine(AppContext.BaseDirectory, "ProtonMailMcp.dll"), "--config", host.config.ConfigPath],
                Name = "SyntheticPhase3", StandardErrorLines = line => host.stderr.Enqueue(line)
            }), new McpClientOptions { InitializationTimeout = TimeSpan.FromSeconds(10), ProtocolVersion = "2025-11-25" });
            return host;
        }
        public async ValueTask DisposeAsync()
        {
            await Client.DisposeAsync();
            config.Dispose();
            foreach (var line in stderr)
                Assert.Matches(@"\Atool: (list_folders|search_messages|get_message) correlation=[a-f0-9]{32} duration_ms=[0-9]+ outcome=[a-z_]+\z", line);
        }
    }
}
