using ProtonMailMcp.Configuration;
using ProtonMailMcp.Contracts;
using ProtonMailMcp.Mail;
using ProtonMailMcp.Tests.Fixtures;
using static ProtonMailMcp.Tests.Fixtures.ScriptedImapServer;

namespace ProtonMailMcp.Tests.Protocol;

[Trait("Category", "Protocol")]
public sealed class MailKitMailboxTests
{
    private const string SummaryFetch = @"UID FETCH 42 \(UID FLAGS INTERNALDATE RFC822.SIZE ENVELOPE BODYSTRUCTURE\)";
    private const string Plain = "(\"TEXT\" \"PLAIN\" (\"CHARSET\" \"UTF-8\") NIL NIL \"7BIT\" 5 1 NIL NIL NIL NIL)";
    private static Step Summary(string structure = Plain) => new(SummaryFetch,
        "* 1 FETCH (UID 42 FLAGS (\\Deleted) INTERNALDATE \"17-Oct-2026 12:00:00 +0000\" RFC822.SIZE 100 " +
        "ENVELOPE (NIL \"synthetic subject\" NIL NIL NIL NIL NIL NIL NIL NIL) BODYSTRUCTURE " + structure + ")\r\n{tag} OK fetched\r\n");
    private static Step Body(string value = "hello", string section = "TEXT") => new(@"UID FETCH 42 \(BODY\.PEEK\[" + section + @"\]\)",
        $"* 1 FETCH (UID 42 BODY[{section}] {{{value.Length}}}\r\n{value})\r\n{{tag}} OK body\r\n");

    [Fact]
    public async Task ReadsDeletedUnreadMessageUsingExamineAndPeekOnly()
    {
        await using var server = new ScriptedImapServer(new Session(Login(Examine(), Summary(), Body(), Logout())));
        using var mailbox = new MailKitMailbox(server.Settings());
        var result = await mailbox.GetMessageAsync(new("INBOX", 7, 42), CancellationToken.None);
        Assert.Equal("hello", result.Text);
        Assert.Equal(ContentStatus.Available, result.ContentStatus);
        Assert.False(result.Summary.Seen);
        Assert.Null(result.Summary.SenderDate);
        await server.VerifyAsync();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BothRequiredTlsModesWork(bool startTls)
    {
        await using var server = new ScriptedImapServer(new Session(Login(Logout()), StartTls: startTls));
        using var mailbox = new MailKitMailbox(server.Settings(startTls ? BridgeTlsMode.StartTls : BridgeTlsMode.SslOnConnect));
        Assert.Single(await mailbox.ListFoldersAsync(CancellationToken.None));
        await server.VerifyAsync();
    }

    [Theory]
    [InlineData("mismatch")]
    [InlineData("expired")]
    [InlineData("future")]
    public async Task InvalidPeerFailsBeforeAuthentication(string mode)
    {
        var start = mode == "future" ? DateTimeOffset.UtcNow.AddDays(1) : DateTimeOffset.UtcNow.AddDays(-2);
        var end = mode == "expired" ? DateTimeOffset.UtcNow.AddDays(-1) : DateTimeOffset.UtcNow.AddDays(2);
        await using var server = new ScriptedImapServer(start, end, new Session([]));
        using var mailbox = new MailKitMailbox(server.Settings(pin: mode == "mismatch" ? new byte[32] : null));
        await ErrorAsync(MailboxError.TlsFailed, () => mailbox.ListFoldersAsync(CancellationToken.None));
        await server.VerifyAsync();
        Assert.Empty(server.Commands);
    }

    [Fact]
    public async Task MissingStartTlsFailsWithoutLoginOrFallback()
    {
        await using var server = new ScriptedImapServer(new Session([], StartTls: true, AdvertiseStartTls: false));
        using var mailbox = new MailKitMailbox(server.Settings(BridgeTlsMode.StartTls));
        await ErrorAsync(MailboxError.TlsFailed, () => mailbox.ListFoldersAsync(CancellationToken.None));
        await server.VerifyAsync();
        Assert.Empty(server.Commands);
    }

    [Fact]
    public async Task DeniedFoldersAndInvalidReferencesNeverConnect()
    {
        await using var server = new ScriptedImapServer();
        using var mailbox = new MailKitMailbox(server.Settings());
        await ErrorAsync(MailboxError.DeniedFolder, () => mailbox.SearchAsync(new("INBOX/child"), CancellationToken.None));
        await ErrorAsync(MailboxError.DeniedFolder, () => mailbox.GetMessageAsync(new("private", 7, 42), CancellationToken.None));
        await ErrorAsync(MailboxError.InvalidInput, () => mailbox.GetMessageAsync(new("INBOX", 0, 42), CancellationToken.None));
        using var empty = new MailKitMailbox(server.Settings(folders: []));
        await ErrorAsync(MailboxError.DeniedFolder, () => empty.ListFoldersAsync(CancellationToken.None));
        await ErrorAsync(MailboxError.DeniedFolder, () => empty.GetMessageAsync(new("INBOX", 7, 42), CancellationToken.None));
        Assert.Equal(0, server.AcceptedConnections);
    }

    [Theory]
    [InlineData("stale", "StaleReference")]
    [InlineData("write", "ProtocolFailure")]
    [InlineData("missing", "MessageNotFound")]
    [InlineData("vanished", "MessageNotFound")]
    [InlineData("fetch-failed", "ProtocolFailure")]
    [InlineData("malformed", "BridgeUnavailable")]
    [InlineData("broken", "BridgeUnavailable")]
    public async Task FailuresDisposeWithoutCloseOrMutationAndNextCallRecovers(string mode, string error)
    {
        var steps = new List<Step> { Examine(access: mode == "write" ? "READ-WRITE" : "READ-ONLY", validity: mode == "stale" ? 8u : 7u) };
        if (mode is not ("stale" or "write"))
            steps.Add(mode switch
            {
                "missing" => new(SummaryFetch, "{tag} OK missing\r\n"),
                "fetch-failed" => new(SummaryFetch, "{tag} NO synthetic sensitive sentinel\r\n"),
                "malformed" => new(SummaryFetch, "* 1 FETCH (UID invalid)\r\n"),
                "broken" => new(SummaryFetch, EndConnection: true),
                _ => Summary()
            });
        if (mode == "vanished") steps.Add(new(@"UID FETCH 42 \(BODY\.PEEK\[TEXT\]\)", "{tag} OK vanished\r\n"));
        await using var server = new ScriptedImapServer(new Session(Login(steps.ToArray())), new Session(Login(Logout())));
        using var mailbox = new MailKitMailbox(server.Settings());
        await ErrorAsync(Enum.Parse<MailboxError>(error), () => mailbox.GetMessageAsync(new("INBOX", 7, 42), CancellationToken.None));
        Assert.Single(await mailbox.ListFoldersAsync(CancellationToken.None));
        await server.VerifyAsync();
        Assert.Equal(2, server.AcceptedConnections);
    }

    [Fact]
    public async Task AuthenticationFailureDoesNotRetryOrLeakServerText()
    {
        await using var server = new ScriptedImapServer(new Session([
            new("LOGIN SYNTHETIC-username-sentinel SYNTHETIC-password-sentinel", "{tag} NO sensitive sentinel\r\n")]));
        using var mailbox = new MailKitMailbox(server.Settings());
        await ErrorAsync(MailboxError.AuthenticationFailed, () => mailbox.ListFoldersAsync(CancellationToken.None));
        await server.VerifyAsync();
        Assert.Single(server.Commands);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OversizeAdvertisedAndActualLiteralAreBounded(bool actual)
    {
        var steps = new List<Step> { Examine(), Summary(actual ? Plain : Plain.Replace("5 1", "1048577 1")) };
        if (actual) steps.Add(new(@"UID FETCH 42 \(BODY\.PEEK\[TEXT\]\)",
            "* 1 FETCH (UID 42 BODY[TEXT] {1048577}\r\n", WaitForDisconnect: true));
        else steps.Add(Logout());
        await using var server = new ScriptedImapServer(new Session(Login(steps.ToArray())));
        using var mailbox = new MailKitMailbox(server.Settings());
        var result = await mailbox.GetMessageAsync(new("INBOX", 7, 42), CancellationToken.None);
        Assert.Equal(ContentStatus.Oversized, result.ContentStatus);
        Assert.Null(result.Text);
        await server.VerifyAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BodyOutputIsBoundedAndHtmlDoesNotFetchExternalReferences(bool html)
    {
        var value = html ? "<html><head><script>evil()</script></head><body><p>Hello &amp; world</p><img src='https://synthetic.invalid/image'></body></html>" : new string('x', 32001);
        var structure = Plain.Replace("5 1", value.Length + " 1");
        if (html) structure = structure.Replace("PLAIN", "HTML");
        await using var server = new ScriptedImapServer(new Session(Login(Examine(), Summary(structure), Body(value), Logout())));
        using var mailbox = new MailKitMailbox(server.Settings());
        var result = await mailbox.GetMessageAsync(new("INBOX", 7, 42), CancellationToken.None);
        Assert.Equal(ContentStatus.Available, result.ContentStatus);
        if (html)
        {
            Assert.Contains("Hello & world", result.Text);
            Assert.DoesNotContain("evil", result.Text);
            Assert.DoesNotContain("https://", result.Text);
            Assert.False(result.Truncated);
        }
        else { Assert.Equal(32000, result.Text!.Length); Assert.True(result.Truncated); }
        await server.VerifyAsync();
    }

    [Fact]
    public async Task MultipartAlternativeSelectsPlainAndExcludesAttachmentPayload()
    {
        const string html = "(\"TEXT\" \"HTML\" (\"CHARSET\" \"UTF-8\") NIL NIL \"7BIT\" 100 1 NIL NIL NIL NIL)";
        const string attachment = "(\"APPLICATION\" \"OCTET-STREAM\" (\"NAME\" \"../synthetic.bin\") NIL NIL \"BASE64\" 900 NIL (\"ATTACHMENT\" (\"FILENAME\" \"../synthetic.bin\")) NIL NIL)";
        var structure = "((" + Plain + html + "\"ALTERNATIVE\")" + attachment + "\"MIXED\")";
        await using var server = new ScriptedImapServer(new Session(Login(Examine(), Summary(structure), Body(section: "1.1"), Logout())));
        using var mailbox = new MailKitMailbox(server.Settings());
        var result = await mailbox.GetMessageAsync(new("INBOX", 7, 42), CancellationToken.None);
        Assert.Equal("hello", result.Text);
        var metadata = Assert.Single(result.Attachments);
        Assert.Equal("../synthetic.bin", metadata.Filename);
        Assert.Equal(900u, metadata.Size);
        await server.VerifyAsync();
    }


    [Theory]
    [InlineData(20, 51, true)]
    [InlineData(50, 51, true)]
    [InlineData(50, 21, false)]
    public async Task SearchUsesTypedSenderDateFiltersUidOrderingAndBoundedSummaries(int limit, int matchesCount, bool truncated)
    {
        var chosen = Enumerable.Range(1, matchesCount).Select(value => value * 2).OrderDescending().Take(limit).Order().ToArray();
        var ids = string.Join(',', chosen.Reverse());
        var response = string.Concat(chosen.Select((uid, index) => $"* {index + 1} FETCH (UID {uid} FLAGS () RFC822.SIZE 100 ENVELOPE (NIL NIL NIL NIL NIL NIL NIL NIL NIL NIL))\r\n"));
        await using var server = new ScriptedImapServer(new Session(Login(Examine(),
            new("UID SEARCH FROM sender OR TO recipient CC recipient SUBJECT subject SENTSINCE 1-Oct-2026 SENTBEFORE 18-Oct-2026 UNSEEN",
                "* SEARCH " + string.Join(' ', Enumerable.Range(1, matchesCount).Select(value => value * 2)) + "\r\n{tag} OK search\r\n"),
            new(@"UID FETCH " + ids + @" \(UID FLAGS INTERNALDATE RFC822.SIZE ENVELOPE\)", response + "{tag} OK fetched\r\n"), Logout())));
        using var mailbox = new MailKitMailbox(server.Settings());
        var request = new SearchRequest("INBOX", "sender", "recipient", "subject", new(2026, 10, 1), new(2026, 10, 18), true);
        if (limit != 20) request = request with { Limit = limit };
        var result = await mailbox.SearchAsync(request, CancellationToken.None);
        Assert.Equal(truncated, result.Truncated);
        Assert.Equal(chosen.Reverse().Select(value => (uint)value), result.Messages.Select(item => item.Reference.Uid));
        Assert.All(result.Messages, item => { Assert.Null(item.InternalDate); Assert.Null(item.Subject); });
        await server.VerifyAsync();
    }

    [Theory]
    [InlineData(true, false, "SENTSINCE 1-Apr-2025")]
    [InlineData(false, true, "SENTBEFORE 2-Apr-2025")]
    [InlineData(true, true, "SENTSINCE 1-Apr-2025 SENTBEFORE 2-Apr-2025")]
    public async Task SenderDateBoundsUseSentKeysDespiteDifferentInternalDate(bool start, bool end, string query)
    {
        // Synthetic imported message: header date and mailbox internal date differ.
        const string response = "* 1 FETCH (UID 42 FLAGS () INTERNALDATE \"20-Aug-2026 12:00:00 +0000\" RFC822.SIZE 100 ENVELOPE (\"Tue, 1 Apr 2025 23:30:00 -0700\" NIL NIL NIL NIL NIL NIL NIL NIL NIL))\r\n{tag} OK fetched\r\n";
        await using var server = new ScriptedImapServer(new Session(Login(Examine(),
            new("UID SEARCH " + query, "* SEARCH 42\r\n{tag} OK search\r\n"),
            new(@"UID FETCH 42 \(UID FLAGS INTERNALDATE RFC822.SIZE ENVELOPE\)", response), Logout())));
        using var mailbox = new MailKitMailbox(server.Settings());
        var result = await mailbox.SearchAsync(new("INBOX",
            SentStart: start ? new DateOnly(2025, 4, 1) : null,
            SentEnd: end ? new DateOnly(2025, 4, 2) : null), CancellationToken.None);
        var message = Assert.Single(result.Messages);
        Assert.Equal(new DateTimeOffset(2025, 4, 1, 23, 30, 0, TimeSpan.FromHours(-7)), message.SenderDate);
        Assert.Equal(new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero), message.InternalDate);
        await server.VerifyAsync();
    }

    [Theory]
    [InlineData("UID SEARCH ALL", null)]
    [InlineData("UID SEARCH SEEN", false)]
    [InlineData("UID SEARCH UNSEEN", true)]
    public async Task UnreadTriStateAndEmptySearch(string command, bool? unread)
    {
        await using var server = new ScriptedImapServer(new Session(Login(Examine(), new(command, "* SEARCH\r\n{tag} OK empty\r\n"), Logout())));
        using var mailbox = new MailKitMailbox(server.Settings());
        var result = await mailbox.SearchAsync(new("inbox", Unread: unread), CancellationToken.None);
        Assert.Empty(result.Messages);
        Assert.False(result.Truncated);
        await server.VerifyAsync();
    }

    [Fact]
    public async Task ListingOmitsMissingNonselectableAndUnapprovedResponses()
    {
        await using var server = new ScriptedImapServer(new Session(Login(
            new("LIST \"\" Missing", "{tag} OK absent\r\n"),
            new("LIST \"\" Parent", "* LIST (\\Noselect) \"/\" Parent\r\n{tag} OK list\r\n"),
            new("LIST \"\" Approved", "* LIST () \"/\" Unapproved\r\n* LIST () \"/\" Approved\r\n{tag} OK list\r\n"), Logout())));
        using var mailbox = new MailKitMailbox(server.Settings(folders: ["INBOX", "Missing", "Parent", "Approved"]));
        var result = await mailbox.ListFoldersAsync(CancellationToken.None);
        Assert.Equal(new[] { "INBOX", "Approved" }, result.Select(item => item.Name));
        await server.VerifyAsync();
    }

    [Fact]
    public async Task CancellationDuringTransferAndWhileQueuedReleasesGateForFreshConnection()
    {
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = new ScriptedImapServer(new Session(Login(Examine(), Summary(),
            new(@"UID FETCH 42 \(BODY\.PEEK\[TEXT\]\)", "* 1 FETCH (UID 42 BODY[TEXT] {5}\r\nhe", WaitForDisconnect: true, Reached: reached))),
            new Session(Login(Logout())));
        using var mailbox = new MailKitMailbox(server.Settings());
        using var firstCancellation = new CancellationTokenSource();
        var first = mailbox.GetMessageAsync(new("INBOX", 7, 42), firstCancellation.Token);
        await reached.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var queuedCancellation = new CancellationTokenSource();
        var queued = mailbox.ListFoldersAsync(queuedCancellation.Token);
        queuedCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
        Assert.Equal(1, server.AcceptedConnections);
        var recovery = mailbox.ListFoldersAsync(CancellationToken.None);
        await Task.Delay(50);
        Assert.False(recovery.IsCompleted);
        Assert.False(server.HasPendingConnection);
        firstCancellation.Cancel();
        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        Assert.Equal(firstCancellation.Token, exception.CancellationToken);
        Assert.Single(await recovery);
        await server.VerifyAsync();
        Assert.Equal(2, server.AcceptedConnections);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeadlineIncludesCleanupAndRecoveryCreatesFreshSession(bool duringLogout)
    {
        var steps = duringLogout ? Login(new Step("LOGOUT", WaitForDisconnect: true)) :
            Login(Examine(), new("UID SEARCH ALL", WaitForDisconnect: true));
        await using var server = new ScriptedImapServer(new Session(steps), new Session(Login(Logout())));
        using var mailbox = new MailKitMailbox(server.Settings()) { OperationDeadline = TimeSpan.FromMilliseconds(500) };
        await ErrorAsync(MailboxError.Timeout, () => duringLogout ? mailbox.ListFoldersAsync(CancellationToken.None) :
            (Task)mailbox.SearchAsync(new("INBOX"), CancellationToken.None));
        Assert.Single(await mailbox.ListFoldersAsync(CancellationToken.None));
        await server.VerifyAsync();
    }


    [Theory]
    [InlineData("missing")]
    [InlineData("nonselectable")]
    [InlineData("examine-denied")]
    public async Task UnavailableFoldersNeverFallBackToSelect(string mode)
    {
        var listing = mode == "missing" ? "{tag} OK absent\r\n" :
            "* LIST (" + (mode == "nonselectable" ? "\\Noselect" : "") + ") \"/\" Approved\r\n{tag} OK list\r\n";
        var steps = new List<Step> { new("LIST \"\" Approved", listing) };
        if (mode == "examine-denied") steps.Add(new("EXAMINE Approved", "{tag} NO cannot examine\r\n"));
        await using var server = new ScriptedImapServer(new Session(Login(steps.ToArray())));
        using var mailbox = new MailKitMailbox(server.Settings(folders: ["INBOX", "Approved"]));
        await ErrorAsync(mode == "examine-denied" ? MailboxError.ProtocolFailure : MailboxError.FolderUnavailable,
            () => mailbox.GetMessageAsync(new("Approved", 7, 42), CancellationToken.None));
        await server.VerifyAsync();
    }

    [Theory]
    [InlineData("attached")]
    [InlineData("unsupported")]
    [InlineData("missing")]
    public async Task MissingAndUnsupportedBodiesReturnMetadataWithoutFetch(string mode)
    {
        var structure = mode switch
        {
            "attached" => Plain.Replace("NIL NIL NIL NIL)", "NIL (\"ATTACHMENT\" (\"FILENAME\" \"synthetic.txt\")) NIL NIL)"),
            "unsupported" => "(\"APPLICATION\" \"OCTET-STREAM\" NIL NIL NIL \"BASE64\" 100 NIL NIL NIL NIL)",
            _ => "NIL"
        };
        await using var server = new ScriptedImapServer(new Session(Login(Examine(), Summary(structure), Logout())));
        using var mailbox = new MailKitMailbox(server.Settings());
        var result = await mailbox.GetMessageAsync(new("INBOX", 7, 42), CancellationToken.None);
        Assert.Null(result.Text);
        Assert.Equal(mode == "missing" ? ContentStatus.Missing : ContentStatus.Unsupported, result.ContentStatus);
        if (mode == "attached") Assert.Single(result.Attachments);
        await server.VerifyAsync();
    }

    [Fact]
    public async Task AttachmentMetadataIsBoundedWithoutFetchingPayloads()
    {
        const string attachment = "(\"APPLICATION\" \"OCTET-STREAM\" NIL NIL NIL \"BASE64\" 100 NIL (\"ATTACHMENT\" (\"FILENAME\" \"synthetic.bin\")) NIL NIL)";
        var structure = "(" + Plain + string.Concat(Enumerable.Repeat(attachment, 101)) + "\"MIXED\")";
        await using var server = new ScriptedImapServer(new Session(Login(Examine(), Summary(structure), Body(section: "1"), Logout())));
        using var mailbox = new MailKitMailbox(server.Settings());
        var result = await mailbox.GetMessageAsync(new("INBOX", 7, 42), CancellationToken.None);
        Assert.Equal(100, result.Attachments.Count);
        Assert.True(result.AttachmentsTruncated);
        await server.VerifyAsync();
    }

    [Fact]
    public async Task ExactlyOneMiBTextTransferSucceedsAndTruncatesOutput()
    {
        var value = new string('x', MimeText.MaxTransferBytes);
        await using var server = new ScriptedImapServer(new Session(Login(Examine(),
            Summary(Plain.Replace("5 1", value.Length + " 1")), Body(value), Logout())));
        using var mailbox = new MailKitMailbox(server.Settings());
        var result = await mailbox.GetMessageAsync(new("INBOX", 7, 42), CancellationToken.None);
        Assert.Equal(ContentStatus.Available, result.ContentStatus);
        Assert.Equal(32000, result.Text!.Length);
        Assert.True(result.Truncated);
        await server.VerifyAsync();
    }


    [Fact]
    public async Task ThirtySecondDeadlineIsArmedWhileQueuedAndTimeoutDoesNotReleaseAnotherCallsGate()
    {
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = new ScriptedImapServer(new Session(Login(Examine(), Summary(),
            new(@"UID FETCH 42 \(BODY\.PEEK\[TEXT\]\)", "* 1 FETCH (UID 42 BODY[TEXT] {5}\r\nhe", WaitForDisconnect: true, Reached: reached))),
            new Session(Login(Logout())));
        var clock = new ManualDeadlineClock();
        using var mailbox = new MailKitMailbox(server.Settings()) { DeadlineClock = clock };
        using var cancellation = new CancellationTokenSource();
        var first = mailbox.GetMessageAsync(new("INBOX", 7, 42), cancellation.Token);
        await reached.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var queued = mailbox.ListFoldersAsync(CancellationToken.None);
        Assert.Equal(2, clock.Timers.Count);
        Assert.All(clock.Timers, timer => Assert.Equal(TimeSpan.FromSeconds(30), timer.DueTime));
        clock.Timers[1].Expire();
        await ErrorAsync(MailboxError.Timeout, () => queued);
        Assert.False(first.IsCompleted);
        var recovery = mailbox.ListFoldersAsync(CancellationToken.None);
        await Task.Delay(50);
        Assert.False(recovery.IsCompleted);
        Assert.False(server.HasPendingConnection);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        Assert.Single(await recovery);
        Assert.All(clock.Timers, timer => Assert.True(timer.Disposed));
        await server.VerifyAsync();
    }

    private sealed class ManualDeadlineClock : TimeProvider
    {
        public List<ManualTimer> Timers { get; } = [];
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(callback, state, dueTime);
            Timers.Add(timer);
            return timer;
        }
        public sealed class ManualTimer(TimerCallback callback, object? state, TimeSpan dueTime) : ITimer
        {
            public TimeSpan DueTime { get; } = dueTime;
            public bool Disposed { get; private set; }
            public void Expire() { if (!Disposed) callback(state); }
            public bool Change(TimeSpan dueTime, TimeSpan period) => !Disposed;
            public void Dispose() => Disposed = true;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }

    private static async Task ErrorAsync(MailboxError expected, Func<Task> operation)
    {
        var exception = await Assert.ThrowsAsync<MailboxException>(operation);
        Assert.Equal(expected, exception.Error);
        Assert.Equal(expected.ToString(), exception.Message);
        Assert.Null(exception.InnerException);
    }
}
