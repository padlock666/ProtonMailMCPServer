using System.Text.RegularExpressions;
using ProtonMailMcp.Configuration;
using ProtonMailMcp.Contracts;
using ProtonMailMcp.Mail;
using ProtonMailMcp.Tests.Fixtures;
using static ProtonMailMcp.Tests.Fixtures.ScriptedImapServer;

namespace ProtonMailMcp.Tests.Protocol;

[Trait("Category", "Protocol")]
public sealed class SearchPaginationTests
{
    private static Step Search(string query, params uint[] matches) => new(Regex.Escape("UID SEARCH " + query),
        "* SEARCH " + string.Join(' ', matches) + "\r\n{tag} OK searched\r\n");
    private static Step Fetch(uint[] candidates, uint[]? surviving = null) => new(
        @"UID FETCH " + string.Join(',', candidates) + @" \(UID FLAGS INTERNALDATE RFC822.SIZE ENVELOPE\)",
        string.Concat((surviving ?? candidates).Select((uid, i) =>
            $"* {i + 1} FETCH (UID {uid} FLAGS () ENVELOPE (NIL NIL NIL NIL NIL NIL NIL NIL NIL NIL))\r\n")) + "{tag} OK fetched\r\n");
    private static Session Page(string query, uint[] matches, uint[] candidates, uint[]? surviving = null) =>
        new(Login(Examine(), Search(query, matches), Fetch(candidates, surviving), Logout()));

    [Theory]
    [InlineData(1, false)]
    [InlineData(20, false)]
    [InlineData(50, true)]
    public async Task TraversesMoreThanFiftySparseMatchesExactlyOnce(int limit, bool allExcept)
    {
        var expected = Enumerable.Range(1, 53).Select(i => (uint)i * 2).Reverse().ToArray();
        var sessions = new List<Session>();
        for (var offset = 0; offset < expected.Length; offset += limit)
        {
            var remaining = expected.Skip(offset).ToArray();
            var query = offset == 0 ? "ALL" : "UID 1:" + (expected[offset - 1] - 1);
            sessions.Add(Page(query, remaining.Reverse().ToArray(), remaining.Take(limit).ToArray()));
        }
        await using var server = new ScriptedImapServer(sessions.ToArray());
        using var mailbox = new MailKitMailbox(server.Settings(folderAccessMode: allExcept ? FolderAccessMode.AllExcept : FolderAccessMode.Allowlist));
        var actual = new List<uint>();
        string? cursor = null;
        do
        {
            var page = await mailbox.SearchAsync(new("INBOX", Limit: limit, Cursor: cursor), CancellationToken.None);
            Assert.InRange(page.Messages.Count, 1, limit);
            Assert.Equal(page.NextCursor is not null, page.Truncated);
            actual.AddRange(page.Messages.Select(m => m.Reference.Uid));
            cursor = page.NextCursor;
        } while (cursor is not null);
        Assert.Equal(expected, actual);
        await server.VerifyAsync();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(50)]
    [InlineData(51)]
    public async Task EmptyExactAndOverflowCountsAgreeWithContinuation(int count)
    {
        var limit = count >= 50 ? 50 : 2;
        var matches = Enumerable.Range(1, count).Select(i => (uint)i * 2).Reverse().ToArray();
        var steps = new List<Step> { Examine(), Search("ALL", matches) };
        if (count > 0) steps.Add(Fetch(matches.Take(limit).ToArray()));
        steps.Add(Logout());
        await using var server = new ScriptedImapServer(new Session(Login(steps.ToArray())));
        using var mailbox = new MailKitMailbox(server.Settings());
        var page = await mailbox.SearchAsync(new("INBOX", Limit: limit), CancellationToken.None);
        Assert.Equal(matches.Take(limit), page.Messages.Select(m => m.Reference.Uid));
        Assert.Equal(count > limit, page.Truncated);
        Assert.Equal(page.Truncated, page.NextCursor is not null);
        await server.VerifyAsync();
    }

    [Fact]
    public async Task DuplicateSearchIdsAndUnsolicitedSummariesCannotRepeatOrBroadenPage()
    {
        await using var server = new ScriptedImapServer(Page("ALL", [10, 30, 50, 50], [50, 30], [10, 30, 50]));
        using var mailbox = new MailKitMailbox(server.Settings());
        var page = await mailbox.SearchAsync(new("INBOX", Limit: 2), CancellationToken.None);
        Assert.Equal(new uint[] { 50, 30 }, page.Messages.Select(m => m.Reference.Uid));
        Assert.True(page.Truncated);
        await server.VerifyAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task VanishedCandidatesStillAdvanceAndNewArrivalsDoNotShiftPages(bool allVanish)
    {
        await using var server = new ScriptedImapServer(
            Page("ALL", [10, 30, 50, 70, 90], [90, 70], allVanish ? [] : [90]),
            // UID 100 has arrived and UID 70 vanished; only UIDs below 70 may match.
            Page("UID 1:69", [10, 30, 50], [50, 30]),
            new Session(Login(Examine(), Search("UID 1:29"), Logout())));
        using var mailbox = new MailKitMailbox(server.Settings());
        var first = await mailbox.SearchAsync(new("INBOX", Limit: 2), CancellationToken.None);
        Assert.Equal(allVanish ? 0 : 1, first.Messages.Count);
        Assert.True(first.Truncated);
        var second = await mailbox.SearchAsync(new("inbox", Limit: 2, Cursor: first.NextCursor), CancellationToken.None);
        Assert.Equal(new uint[] { 50, 30 }, second.Messages.Select(m => m.Reference.Uid));
        var last = await mailbox.SearchAsync(new("INBOX", Cursor: second.NextCursor), CancellationToken.None);
        Assert.Empty(last.Messages); Assert.False(last.Truncated); Assert.Null(last.NextCursor);
        await server.VerifyAsync();
    }

    [Fact]
    public async Task ReplayedCursorReevaluatesUnreadMatchesAndAllowsPageSizeChange()
    {
        var codec = new SearchCursorCodec();
        var request = new SearchRequest("INBOX", Unread: true);
        var cursor = codec.Encode(request, 7, 90);
        await using var server = new ScriptedImapServer(
            Page("UNSEEN UID 1:89", [10, 30, 50], [50]),
            Page("UNSEEN UID 1:89", [10, 30, 70], [70, 30, 10]));
        using var mailbox = new MailKitMailbox(server.Settings(), codec);
        var first = await mailbox.SearchAsync(request with { Limit = 1, Cursor = cursor }, CancellationToken.None);
        Assert.Equal(50u, Assert.Single(first.Messages).Reference.Uid);
        var replay = await mailbox.SearchAsync(request with { Limit = 50, Cursor = cursor }, CancellationToken.None);
        Assert.Equal(new uint[] { 70, 30, 10 }, replay.Messages.Select(m => m.Reference.Uid));
        Assert.Null(replay.NextCursor);
        await server.VerifyAsync();
    }

    [Theory]
    [InlineData(1u)]
    [InlineData(uint.MaxValue)]
    public async Task ExtremeBoundariesStayFiniteAndNeverEmitZeroOrStar(uint boundary)
    {
        var codec = new SearchCursorCodec();
        var request = new SearchRequest("INBOX");
        var operation = boundary == 1 ? new[] { Examine(), Logout() } :
            new[] { Examine(), Search("UID 1:4294967294"), Logout() };
        await using var server = new ScriptedImapServer(new Session(Login(operation)));
        using var mailbox = new MailKitMailbox(server.Settings(), codec);
        var result = await mailbox.SearchAsync(request with { Cursor = codec.Encode(request, 7, boundary) }, CancellationToken.None);
        Assert.Empty(result.Messages); Assert.Null(result.NextCursor);
        await server.VerifyAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FolderPolicyAndBindingAreEnforcedBeforeAnyConnection(bool allExcept)
    {
        var codec = new SearchCursorCodec();
        var request = new SearchRequest("Private");
        await using var server = new ScriptedImapServer();
        using var mailbox = new MailKitMailbox(server.Settings(folderAccessMode: allExcept ? FolderAccessMode.AllExcept : FolderAccessMode.Allowlist,
            excludedFolders: ["Private"]), codec);
        await Error(MailboxError.DeniedFolder, () => mailbox.SearchAsync(request with { Cursor = codec.Encode(request, 7, 42) }, CancellationToken.None));
        var allowed = new SearchRequest("INBOX");
        foreach (var bad in new[] { "", new string('a', 98), codec.Encode(allowed with { Subject = "other" }, 7, 42),
            new SearchCursorCodec().Encode(allowed, 7, 42) })
            await Error(MailboxError.InvalidInput, () => mailbox.SearchAsync(allowed with { Cursor = bad }, CancellationToken.None));
        Assert.Equal(0, server.AcceptedConnections);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("nonselectable")]
    [InlineData("alias")]
    public async Task ContinuationNeverOpensUnavailableOrUnexpectedResolvedFolder(string mode)
    {
        var codec = new SearchCursorCodec();
        var request = new SearchRequest("Work");
        var listing = mode switch
        {
            "missing" => "{tag} OK missing\r\n",
            "nonselectable" => "* LIST (\\Noselect) \"/\" Work\r\n{tag} OK list\r\n",
            _ => "* LIST () \"/\" INBOX\r\n{tag} OK list\r\n"
        };
        await using var server = new ScriptedImapServer(new Session(Login(new Step("LIST \"\" Work", listing))),
            new Session(Login(new Step("LIST \"\" \"\\*\"", "{tag} OK list\r\n"), Logout())));
        using var mailbox = new MailKitMailbox(server.Settings(folderAccessMode: FolderAccessMode.AllExcept, excludedFolders: ["INBOX"]), codec);
        await Error(MailboxError.FolderUnavailable, () => mailbox.SearchAsync(request with { Cursor = codec.Encode(request, 7, 90) }, CancellationToken.None));
        Assert.Empty(await mailbox.ListFoldersAsync(CancellationToken.None));
        await server.VerifyAsync();
    }

    [Theory]
    [InlineData("stale", "StaleReference")]
    [InlineData("range", "ProtocolFailure")]
    [InlineData("search", "ProtocolFailure")]
    [InlineData("fetch", "ProtocolFailure")]
    [InlineData("broken", "BridgeUnavailable")]
    [InlineData("malformed", "BridgeUnavailable")]
    public async Task ContinuationFailuresReturnNoPageAndReleaseConnection(string mode, string error)
    {
        var codec = new SearchCursorCodec();
        var request = new SearchRequest("INBOX");
        var steps = new List<Step> { Examine(validity: mode == "stale" ? 8u : 7u) };
        if (mode == "range") steps.Add(Search("UID 1:89", 90));
        if (mode == "search") steps.Add(new("UID SEARCH UID 1:89", "{tag} NO rejected\r\n"));
        if (mode == "broken") steps.Add(new("UID SEARCH UID 1:89", EndConnection: true));
        if (mode == "malformed") steps.Add(new("UID SEARCH UID 1:89", "* SEARCH invalid\r\n", WaitForDisconnect: true));
        if (mode == "fetch")
        {
            steps.Add(Search("UID 1:89", 50));
            steps.Add(new(@"UID FETCH 50 \(UID FLAGS INTERNALDATE RFC822.SIZE ENVELOPE\)", "{tag} NO rejected\r\n"));
        }
        await using var server = new ScriptedImapServer(new Session(Login(steps.ToArray())), new Session(Login(Logout())));
        using var mailbox = new MailKitMailbox(server.Settings(), codec);
        await Error(Enum.Parse<MailboxError>(error), () => mailbox.SearchAsync(request with { Cursor = codec.Encode(request, 7, 90) }, CancellationToken.None));
        Assert.Single(await mailbox.ListFoldersAsync(CancellationToken.None));
        await server.VerifyAsync();
    }

    [Theory]
    [InlineData("search", false)]
    [InlineData("fetch", false)]
    [InlineData("logout", false)]
    [InlineData("search", true)]
    [InlineData("logout", true)]
    public async Task ContinuationCancellationAndTimeoutIncludeQueueAndCleanup(string stage, bool timeout)
    {
        var codec = new SearchCursorCodec();
        var request = new SearchRequest("INBOX");
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var steps = new List<Step> { Examine() };
        if (stage == "search") steps.Add(new("UID SEARCH UID 1:89", WaitForDisconnect: true, Reached: reached));
        else
        {
            steps.Add(Search("UID 1:89", 50));
            if (stage == "fetch") steps.Add(new(@"UID FETCH 50 \(UID FLAGS INTERNALDATE RFC822.SIZE ENVELOPE\)", WaitForDisconnect: true, Reached: reached));
            else { steps.Add(Fetch([50])); steps.Add(new("LOGOUT", WaitForDisconnect: true, Reached: reached)); }
        }
        await using var server = new ScriptedImapServer(new Session(Login(steps.ToArray())), new Session(Login(Logout())));
        using var mailbox = new MailKitMailbox(server.Settings(), codec)
            { OperationDeadline = timeout ? TimeSpan.FromMilliseconds(750) : TimeSpan.FromSeconds(30) };
        using var cancellation = new CancellationTokenSource();
        var call = mailbox.SearchAsync(request with { Cursor = codec.Encode(request, 7, 90) }, cancellation.Token);
        await reached.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var queuedCancellation = new CancellationTokenSource();
        var queued = mailbox.SearchAsync(request with { Cursor = codec.Encode(request, 7, 90) }, queuedCancellation.Token);
        queuedCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
        Assert.Equal(1, server.AcceptedConnections);
        if (timeout) await Error(MailboxError.Timeout, () => call);
        else { cancellation.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call); }
        Assert.Single(await mailbox.ListFoldersAsync(CancellationToken.None));
        await server.VerifyAsync();
    }

    private static async Task Error(MailboxError expected, Func<Task> action) =>
        Assert.Equal(expected, (await Assert.ThrowsAsync<MailboxException>(action)).Error);
}
