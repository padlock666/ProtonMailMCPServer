using System.Text;
using ProtonMailMcp.Contracts;
using ProtonMailMcp.Mail;
using ProtonMailMcp.Tests.Fixtures;
using static ProtonMailMcp.Tests.Fixtures.ScriptedImapServer;

namespace ProtonMailMcp.Tests.Protocol;

[Trait("Category", "Protocol")]
public sealed class AttachmentDownloadTests
{
    private const string Plain = "(\"TEXT\" \"PLAIN\" (\"CHARSET\" \"UTF-8\") NIL NIL \"7BIT\" 5 1 NIL NIL NIL NIL)";
    internal static string Pdf(int length, string encoding = "BASE64", string filename = "../synthetic.pdf") =>
        $"(\"APPLICATION\" \"PDF\" NIL NIL NIL \"{encoding}\" {length} NIL (\"ATTACHMENT\" (\"FILENAME\" \"{filename}\")) NIL NIL)";
    internal static Step Structure(string body) => new(@"UID FETCH 42 \(UID BODYSTRUCTURE\)",
        "* 1 FETCH (UID 42 BODYSTRUCTURE " + body + ")\r\n{tag} OK structure\r\n");
    internal static Step Payload(string value, string section = "TEXT") => new(@"UID FETCH 42 \(BODY\.PEEK\[" + section + @"\]\)",
        $"* 1 FETCH (UID 42 BODY[{section}] {{{value.Length}}}\r\n{value})\r\n{{tag}} OK fetched\r\n");
    private static AttachmentRequest Request(string id = "1", uint validity = 7, string folder = "INBOX") => new(new(folder, validity, 42), id);

    [Theory]
    [InlineData("base64", "%PDF-1.7\r\nsynthetic\r\n%%EOF")]
    [InlineData("quoted-printable", "%PDF-1.7\r\nsynthetic")]
    [InlineData("binary", "synthetic bytes")]
    [InlineData("7bit", "")]
    [InlineData("8bit", "data")]
    public async Task TopLevelPdfBytesHashAndPrivatePathAreExact(string encoding, string data)
    {
        var encoded = encoding == "base64" ? Convert.ToBase64String(Encoding.ASCII.GetBytes(data)) : data;
        using var config = new SyntheticConfiguration();
        var path = config.EnableDownloads();
        using var store = new PrivateDownloadStore(path);
        await using var server = new ScriptedImapServer(new Session(Login(Examine(), Structure(Pdf(encoded.Length, encoding)), Payload(encoded), Logout())));
        using var mailbox = new MailKitMailbox(server.Settings(downloadDirectory: path), downloadStore: store);
        var result = await mailbox.DownloadAttachmentAsync(Request(), default);
        Assert.Equal(Encoding.ASCII.GetBytes(data), await File.ReadAllBytesAsync(result.Path));
        Assert.Equal(data.Length, result.SizeBytes);
        Assert.Equal((uint)encoded.Length, result.ReportedSize);
        Assert.Equal(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.ASCII.GetBytes(data))), result.Sha256);
        Assert.Equal("../synthetic.pdf", result.OriginalFilename);
        Assert.Equal(path, Path.GetDirectoryName(result.Path));
        Assert.EndsWith(".pdf", result.Path);
        Assert.Single(Directory.GetFiles(path));
        await server.VerifyAsync();
    }

    [Fact]
    public async Task MetadataIdsAndNestedDownloadUseOnlySelectedSectionAndAllowRepeatAfterRestart()
    {
        const string encoded = "JVBERg==";
        var body = "(" + Plain + "(" + Pdf(encoded.Length) + "\"MIXED\")\"MIXED\")";
        using var config = new SyntheticConfiguration();
        var path = config.EnableDownloads();
        using var store = new PrivateDownloadStore(path);
        var summary = new Step(@"UID FETCH 42 \(UID FLAGS INTERNALDATE RFC822.SIZE ENVELOPE BODYSTRUCTURE\)",
            "* 1 FETCH (UID 42 FLAGS () ENVELOPE (NIL NIL NIL NIL NIL NIL NIL NIL NIL NIL) BODYSTRUCTURE " + body + ")\r\n{tag} OK structure\r\n");
        await using var server = new ScriptedImapServer(new Session(Login(Examine(), summary, Payload("hello", "1"), Logout())),
            new Session(Login(Examine(), Structure(body), Payload(encoded, "2.1"), Logout())),
            new Session(Login(Examine(), Structure(body), Payload(encoded, "2.1"), Logout())));
        string id;
        string first;
        using (var mailbox = new MailKitMailbox(server.Settings(downloadDirectory: path), downloadStore: store))
        {
            var message = await mailbox.GetMessageAsync(new("INBOX", 7, 42), default);
            id = Assert.Single(message.Attachments).AttachmentId!;
            Assert.Equal("2.1", id);
            first = (await mailbox.DownloadAttachmentAsync(Request(id), default)).Path;
        }
        using var restarted = new MailKitMailbox(server.Settings(downloadDirectory: path), downloadStore: store);
        Assert.NotEqual(first, (await restarted.DownloadAttachmentAsync(Request(id), default)).Path);
        Assert.Equal(2, Directory.GetFiles(path).Length);
        await server.VerifyAsync();
    }

    [Theory]
    [InlineData("TEXT")]
    [InlineData("1.HEADER")]
    [InlineData("0")]
    [InlineData("01")]
    [InlineData("1..2")]
    [InlineData("1] STORE")]
    [InlineData("4294967296")]
    public async Task InputRejectedBeforeNetworkOrDisk(string id)
    {
        await using var server = new ScriptedImapServer();
        using var mailbox = new MailKitMailbox(server.Settings());
        Assert.Equal(MailboxError.InvalidInput, (await Assert.ThrowsAsync<MailboxException>(() => mailbox.DownloadAttachmentAsync(Request(id), default))).Error);
        Assert.Equal(0, server.AcceptedConnections);
    }

    [Theory]
    [InlineData("disabled", "AttachmentDownloadsDisabled")]
    [InlineData("denied", "DeniedFolder")]
    [InlineData("storage", "DownloadDirectoryUnsafe")]
    public async Task PolicyAndStorageFailuresPrecedeConnection(string mode, string expected)
    {
        using var config = new SyntheticConfiguration();
        var path = config.EnableDownloads();
        using var store = new PrivateDownloadStore(path);
        await using var server = new ScriptedImapServer();
        using var mailbox = new MailKitMailbox(server.Settings(downloadDirectory: mode == "disabled" ? null : path), downloadStore: store);
        if (mode == "storage") Directory.CreateDirectory(Path.Combine(path, "foreign"));
        Assert.Equal(Enum.Parse<MailboxError>(expected), (await Assert.ThrowsAsync<MailboxException>(() =>
            mailbox.DownloadAttachmentAsync(Request(folder: mode == "denied" ? "Forbidden" : "INBOX"), default))).Error);
        Assert.Equal(0, server.AcceptedConnections);
    }

    [Theory]
    [InlineData("stale", "StaleReference")]
    [InlineData("missing", "MessageNotFound")]
    [InlineData("body", "AttachmentNotFound")]
    [InlineData("inline", "AttachmentNotFound")]
    [InlineData("encoding", "AttachmentUnsupported")]
    [InlineData("oversized", "AttachmentTooLarge")]
    [InlineData("container", "AttachmentUnsupported")]
    [InlineData("contained", "AttachmentNotFound")]
    public async Task IdentityAndEligibilityFailuresNeverFetchContent(string mode, string expected)
    {
        var steps = new List<Step> { Examine() };
        if (mode != "stale") steps.Add(mode == "missing" ? new(@"UID FETCH 42 \(UID BODYSTRUCTURE\)", "{tag} OK none\r\n") : Structure(mode switch
        {
            "body" => Plain,
            "inline" => Pdf(8).Replace("ATTACHMENT", "INLINE"),
            "encoding" => Pdf(8, "UNKNOWN"),
            "oversized" => Pdf(PrivateDownloadStore.MaxEncodedBytes + 1),
            "container" or "contained" => "(" + Pdf(8) + "\"MIXED\" NIL (\"ATTACHMENT\" NIL) NIL NIL)",
            _ => Pdf(8)
        }));
        using var config = new SyntheticConfiguration();
        var path = config.EnableDownloads();
        using var store = new PrivateDownloadStore(path);
        await using var server = new ScriptedImapServer(new Session(Login(steps.ToArray())), new Session(Login(Logout())));
        using var mailbox = new MailKitMailbox(server.Settings(downloadDirectory: path), downloadStore: store);
        Assert.Equal(Enum.Parse<MailboxError>(expected), (await Assert.ThrowsAsync<MailboxException>(() => mailbox.DownloadAttachmentAsync(
            Request(mode == "contained" ? "2" : "1", mode == "stale" ? 8u : 7u), default))).Error);
        Assert.Empty(Directory.GetFiles(path));
        await mailbox.ListFoldersAsync(default);
        await server.VerifyAsync();
    }

    [Theory]
    [InlineData("malformed", "AttachmentMalformed")]
    [InlineData("mismatch", "AttachmentMalformed")]
    [InlineData("rejected", "ProtocolFailure")]
    [InlineData("broken", "BridgeUnavailable")]
    [InlineData("literal", "AttachmentTooLarge")]
    [InlineData("wrongpart", "ProtocolFailure")]
    public async Task FetchFailuresPublishNothingAndRecover(string mode, string expected)
    {
        var value = mode == "malformed" ? "@@@@" : "YQ==";
        var payload = mode switch
        {
            "rejected" => new Step(@"UID FETCH 42 \(BODY\.PEEK\[TEXT\]\)", "{tag} NO refused\r\n"),
            "broken" => new Step(@"UID FETCH 42 \(BODY\.PEEK\[TEXT\]\)", EndConnection: true),
            "literal" => new Step(@"UID FETCH 42 \(BODY\.PEEK\[TEXT\]\)", $"* 1 FETCH (UID 42 BODY[TEXT] {{{PrivateDownloadStore.MaxEncodedBytes + 1}}}\r\n", WaitForDisconnect: true),
            "wrongpart" => new Step(@"UID FETCH 42 \(BODY\.PEEK\[TEXT\]\)", "* 1 FETCH (UID 42 BODY[2] {4}\r\nYQ==)\r\n{tag} OK fetched\r\n"),
            _ => Payload(value)
        };
        using var config = new SyntheticConfiguration();
        var path = config.EnableDownloads();
        using var store = new PrivateDownloadStore(path);
        await using var server = new ScriptedImapServer(new Session(Login(Examine(), Structure(Pdf(mode == "mismatch" ? 8 : 4)), payload)),
            new Session(Login(Logout())));
        using var mailbox = new MailKitMailbox(server.Settings(downloadDirectory: path), downloadStore: store);
        var error = await Assert.ThrowsAsync<MailboxException>(() => mailbox.DownloadAttachmentAsync(Request(), default));
        Assert.Equal(Enum.Parse<MailboxError>(expected), error.Error);
        Assert.Null(error.InnerException);
        Assert.Empty(Directory.GetFiles(path));
        await mailbox.ListFoldersAsync(default);
        await server.VerifyAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationDuringPayloadOrLogoutRemovesIncompleteFileAndReleasesGate(bool logout)
    {
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var steps = new List<Step> { Examine(), Structure(Pdf(4)) };
        if (logout) { steps.Add(Payload("YQ==")); steps.Add(new("LOGOUT", WaitForDisconnect: true, Reached: reached)); }
        else steps.Add(new(@"UID FETCH 42 \(BODY\.PEEK\[TEXT\]\)", "* 1 FETCH (UID 42 BODY[TEXT] {4}\r\nY", WaitForDisconnect: true, Reached: reached));
        using var config = new SyntheticConfiguration();
        var path = config.EnableDownloads();
        using var store = new PrivateDownloadStore(path);
        await using var server = new ScriptedImapServer(new Session(Login(steps.ToArray())), new Session(Login(Logout())));
        using var mailbox = new MailKitMailbox(server.Settings(downloadDirectory: path), downloadStore: store);
        using var cancel = new CancellationTokenSource();
        var call = mailbox.DownloadAttachmentAsync(Request(), cancel.Token);
        await reached.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
        Assert.Empty(Directory.GetFiles(path));
        await mailbox.ListFoldersAsync(default);
        await server.VerifyAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EmptyPayloadDistinguishesVanishedMessageFromMissingPart(bool messageExists)
    {
        using var config = new SyntheticConfiguration();
        var path = config.EnableDownloads();
        using var store = new PrivateDownloadStore(path);
        await using var server = new ScriptedImapServer(new Session(Login(Examine(), Structure(Pdf(4)),
            new(@"UID FETCH 42 \(BODY\.PEEK\[TEXT\]\)", "{tag} OK none\r\n"),
            new("UID FETCH 42 UID", (messageExists ? "* 1 FETCH (UID 42)\r\n" : "") + "{tag} OK uid\r\n"))));
        using var mailbox = new MailKitMailbox(server.Settings(downloadDirectory: path), downloadStore: store);
        Assert.Equal(messageExists ? MailboxError.AttachmentNotFound : MailboxError.MessageNotFound,
            (await Assert.ThrowsAsync<MailboxException>(() => mailbox.DownloadAttachmentAsync(Request(), default))).Error);
        Assert.Empty(Directory.GetFiles(path));
        await server.VerifyAsync();
    }

    [Theory]
    [InlineData("uid")]
    [InlineData("duplicate")]
    [InlineData("offset")]
    public async Task UnsolicitedPayloadCannotBeAcceptedOrMultiplyAllocation(string mode)
    {
        var response = mode switch
        {
            "uid" => "* 1 FETCH (BODY[TEXT] {4}\r\nYQ== UID 43)\r\n{tag} OK data\r\n",
            "offset" => "* 1 FETCH (UID 42 BODY[TEXT]<1> {4}\r\nYQ==)\r\n{tag} OK data\r\n",
            _ => "* 1 FETCH (UID 42 BODY[TEXT] {4}\r\nYQ== BODY[TEXT] {4}\r\nYQ==)\r\n{tag} OK data\r\n"
        };
        using var config = new SyntheticConfiguration();
        var path = config.EnableDownloads();
        using var store = new PrivateDownloadStore(path);
        await using var server = new ScriptedImapServer(new Session(Login(Examine(), Structure(Pdf(4)),
            new(@"UID FETCH 42 \(BODY\.PEEK\[TEXT\]\)", response))));
        using var mailbox = new MailKitMailbox(server.Settings(downloadDirectory: path), downloadStore: store);
        Assert.Equal(MailboxError.ProtocolFailure, (await Assert.ThrowsAsync<MailboxException>(() => mailbox.DownloadAttachmentAsync(Request(), default))).Error);
        Assert.Empty(Directory.GetFiles(path));
        await server.VerifyAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EncodedBoundaryAndDecodedOverflowAreEnforcedOnRealAdapter(bool overflow)
    {
        var value = overflow ? new string('x', PrivateDownloadStore.MaxFileBytes + 1) :
            new string(' ', PrivateDownloadStore.MaxEncodedBytes - 4) + "YQ==";
        using var config = new SyntheticConfiguration();
        var path = config.EnableDownloads();
        using var store = new PrivateDownloadStore(path);
        var steps = new List<Step> { Examine(), Structure(Pdf(value.Length, overflow ? "BINARY" : "BASE64")), Payload(value) };
        if (!overflow) steps.Add(Logout());
        await using var server = new ScriptedImapServer(new Session(Login(steps.ToArray())));
        using var mailbox = new MailKitMailbox(server.Settings(downloadDirectory: path), downloadStore: store);
        if (overflow)
        {
            Assert.Equal(MailboxError.AttachmentTooLarge, (await Assert.ThrowsAsync<MailboxException>(() => mailbox.DownloadAttachmentAsync(Request(), default))).Error);
            Assert.Empty(Directory.GetFiles(path));
        }
        else
        {
            var result = await mailbox.DownloadAttachmentAsync(Request(), default);
            Assert.Equal("a", await File.ReadAllTextAsync(result.Path));
        }
        await server.VerifyAsync();
    }

    [Fact]
    public async Task QueuedCancellationDoesNotStartAnotherConnectionOrReleaseTheActiveGate()
    {
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var config = new SyntheticConfiguration();
        var path = config.EnableDownloads();
        using var store = new PrivateDownloadStore(path);
        await using var server = new ScriptedImapServer(new Session(Login(Examine(), Structure(Pdf(4)), Payload("YQ=="),
            new("LOGOUT", WaitForDisconnect: true, Reached: reached))), new Session(Login(Logout())));
        using var mailbox = new MailKitMailbox(server.Settings(downloadDirectory: path), downloadStore: store);
        using var activeCancel = new CancellationTokenSource();
        var active = mailbox.DownloadAttachmentAsync(Request(), activeCancel.Token);
        await reached.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var queuedCancel = new CancellationTokenSource();
        var queued = mailbox.DownloadAttachmentAsync(Request(), queuedCancel.Token);
        queuedCancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
        Assert.False(active.IsCompleted);
        Assert.False(server.HasPendingConnection);
        activeCancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => active);
        Assert.Empty(Directory.GetFiles(path));
        await mailbox.ListFoldersAsync(default);
        await server.VerifyAsync();
    }
}
