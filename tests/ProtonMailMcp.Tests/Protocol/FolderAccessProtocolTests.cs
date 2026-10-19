using ProtonMailMcp.Configuration;
using ProtonMailMcp.Contracts;
using ProtonMailMcp.Mail;
using ProtonMailMcp.Tests.Fixtures;
using static ProtonMailMcp.Tests.Fixtures.ScriptedImapServer;

namespace ProtonMailMcp.Tests.Protocol;

[Trait("Category", "Protocol")]
public sealed class FolderAccessProtocolTests
{
    private static Step ListAll(string entries) => new("LIST \"\" \"\\*\"", entries + "{tag} OK list\r\n");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ListsSelectableUnsubscribedLabelsAndNewFoldersOnEveryCall(bool exclusions)
    {
        const string first = "* LIST (\\HasNoChildren) \"/\" INBOX\r\n" +
            "* LIST (\\Noselect) \"/\" Labels\r\n" +
            "* LIST () \"/\" Labels/Work\r\n" +
            "* LIST () \"/\" Labels/Private\r\n" +
            "* LIST () \"/\" Labels/Private/Child\r\n" +
            "* LIST () \"/\" Labels/Caf&AOk-\r\n" +
            "* LIST (\\NonExistent) \"/\" Gone\r\n";
        await using var server = new ScriptedImapServer(
            new Session(Login(ListAll(first), Logout())),
            new Session(Login(ListAll(first + "* LIST () \"/\" Labels/New\r\n"), Logout())));
        using var mailbox = new MailKitMailbox(server.Settings(folderAccessMode: FolderAccessMode.AllExcept,
            excludedFolders: exclusions ? ["inbox", "Labels/Private"] : []));
        var expected = exclusions ? new[] { "Labels/Work", "Labels/Private/Child", "Labels/Café" } :
            ["INBOX", "Labels/Work", "Labels/Private", "Labels/Private/Child", "Labels/Café"];
        Assert.Equal(expected, (await mailbox.ListFoldersAsync(CancellationToken.None)).Select(f => f.Name));
        Assert.Equal(expected.Append("Labels/New"), (await mailbox.ListFoldersAsync(CancellationToken.None)).Select(f => f.Name));
        await server.VerifyAsync();
    }

    [Fact]
    public async Task OversizedDiscoveryFailsWithoutReturningPartialListAndRecovers()
    {
        var entries = string.Concat(Enumerable.Range(1, 4096).Select(i => $"* LIST () \"/\" Label{i}\r\n"));
        await using var server = new ScriptedImapServer(new Session(Login(ListAll(entries))),
            new Session(Login(ListAll(""), Logout())));
        using var mailbox = new MailKitMailbox(server.Settings(folderAccessMode: FolderAccessMode.AllExcept));
        // INBOX plus 4096 distinct labels exceeds the documented 4096-folder output limit.
        await Error(MailboxError.ProtocolFailure, () => mailbox.ListFoldersAsync(CancellationToken.None));
        Assert.Equal("INBOX", Assert.Single(await mailbox.ListFoldersAsync(CancellationToken.None)).Name);
        await server.VerifyAsync();
    }

    [Fact]
    public async Task DiscoveryAllowsExactly4096PermittedFoldersAfterFiltering()
    {
        var entries = "* LIST () \"/\" Private\r\n* LIST (\\Noselect) \"/\" Labels\r\n" +
            string.Concat(Enumerable.Range(1, 4095).Select(i => $"* LIST () \"/\" Label{i}\r\n"));
        await using var server = new ScriptedImapServer(new Session(Login(ListAll(entries), Logout())));
        using var mailbox = new MailKitMailbox(server.Settings(folderAccessMode: FolderAccessMode.AllExcept,
            excludedFolders: ["Private"]));
        Assert.Equal(4096, (await mailbox.ListFoldersAsync(CancellationToken.None)).Count);
        await server.VerifyAsync();
    }

    [Fact]
    public async Task EnumeratesAllAdvertisedNamespacesAndDeduplicatesFolders()
    {
        await using var server = new ScriptedImapServer(new Session([
            new("LOGIN SYNTHETIC-username-sentinel SYNTHETIC-password-sentinel", "{tag} OK [CAPABILITY IMAP4rev1 NAMESPACE] authenticated\r\n"),
            new("NAMESPACE", "* NAMESPACE ((\"\" \"/\")) ((\"Other/\" \"/\")) ((\"Shared/\" \"/\"))\r\n{tag} OK namespace\r\n"),
            new("LIST \"\" \"INBOX\"", "* LIST () \"/\" INBOX\r\n{tag} OK list\r\n"),
            ListAll("* LIST () \"/\" INBOX\r\n* LIST () \"/\" Work\r\n* LIST () \"/\" Work\r\n"),
            new("LIST \"\" \"Shared/\\*\"", "* LIST () \"/\" Shared/Team\r\n{tag} OK list\r\n"),
            new("LIST \"\" \"Other/\\*\"", "* LIST () \"/\" Other/Peer\r\n{tag} OK list\r\n"),
            Logout()
        ]));
        using var mailbox = new MailKitMailbox(server.Settings(folderAccessMode: FolderAccessMode.AllExcept));
        Assert.Equal(new[] { "INBOX", "Work", "Shared/Team", "Other/Peer" },
            (await mailbox.ListFoldersAsync(CancellationToken.None)).Select(f => f.Name));
        await server.VerifyAsync();
    }

    [Fact]
    public async Task ExcludedSearchAndDirectReadFailBeforeConnecting()
    {
        await using var server = new ScriptedImapServer();
        using var mailbox = new MailKitMailbox(server.Settings(folderAccessMode: FolderAccessMode.AllExcept,
            excludedFolders: ["inbox", "Labels/Private"]));
        foreach (var folder in new[] { "INBOX", "Inbox", "Labels/Private" })
        {
            await Error(MailboxError.DeniedFolder, () => mailbox.SearchAsync(new(folder), CancellationToken.None));
            await Error(MailboxError.DeniedFolder, () => mailbox.GetMessageAsync(new(folder, 7, 42), CancellationToken.None));
        }
        await Error(MailboxError.InvalidInput, () => mailbox.SearchAsync(new("Bad\nName"), CancellationToken.None));
        Assert.Equal(0, server.AcceptedConnections);
        await server.VerifyAsync();
    }

    [Fact]
    public async Task NewLabelSearchAndReadUseExamineAndPeekWithoutPriorDiscovery()
    {
        const string label = "Labels/New";
        var lookup = new Step("LIST \"\" " + label, "* LIST () \"/\" " + label + "\r\n{tag} OK list\r\n");
        var parent = new Step("LIST \"\" Labels", "* LIST (\\Noselect) \"/\" Labels\r\n{tag} OK list\r\n");
        var examine = Examine() with { Command = "EXAMINE " + label };
        await using var server = new ScriptedImapServer(
            new Session(Login(lookup, parent, examine, new("UID SEARCH ALL", "* SEARCH\r\n{tag} OK empty\r\n"), Logout())),
            new Session(Login(lookup, parent, examine,
                new(@"UID FETCH 42 \(UID FLAGS INTERNALDATE RFC822.SIZE ENVELOPE BODYSTRUCTURE\)",
                    "* 1 FETCH (UID 42 FLAGS () ENVELOPE (NIL NIL NIL NIL NIL NIL NIL NIL NIL NIL) " +
                    "BODYSTRUCTURE (\"TEXT\" \"PLAIN\" (\"CHARSET\" \"UTF-8\") NIL NIL \"7BIT\" 5 1 NIL NIL NIL NIL))\r\n{tag} OK fetched\r\n"),
                new(@"UID FETCH 42 \(BODY\.PEEK\[TEXT\]\)", "* 1 FETCH (UID 42 BODY[TEXT] {5}\r\nhello)\r\n{tag} OK body\r\n"), Logout())));
        using var mailbox = new MailKitMailbox(server.Settings(folderAccessMode: FolderAccessMode.AllExcept,
            excludedFolders: ["Labels"]));
        Assert.Empty((await mailbox.SearchAsync(new(label), CancellationToken.None)).Messages);
        var result = await mailbox.GetMessageAsync(new(label, 7, 42), CancellationToken.None);
        Assert.Equal("hello", result.Text);
        Assert.Equal(label, result.Summary.Reference.Folder);
        await server.VerifyAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnexpectedResolvedFolderNeverOpensExcludedInbox(bool read)
    {
        // MailKit rejects a lookup that returns only a different folder; no EXAMINE is permitted.
        await using var server = new ScriptedImapServer(new Session(Login(
            new Step("LIST \"\" Alias", "* LIST () \"/\" INBOX\r\n{tag} OK list\r\n"))));
        using var mailbox = new MailKitMailbox(server.Settings(folderAccessMode: FolderAccessMode.AllExcept,
            excludedFolders: ["INBOX"]));
        await Error(MailboxError.FolderUnavailable, () => read ?
            mailbox.GetMessageAsync(new("Alias", 7, 42), CancellationToken.None) :
            (Task)mailbox.SearchAsync(new("Alias"), CancellationToken.None));
        await server.VerifyAsync();
    }

    [Theory]
    [InlineData("rejected")]
    [InlineData("broken")]
    [InlineData("cancelled")]
    public async Task DiscoveryFailureDisposesAndNextCallUsesFreshConnection(string failure)
    {
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failing = failure switch
        {
            "rejected" => ListAll("") with { Response = "{tag} NO SYNTHETIC-sensitive-error\r\n" },
            "broken" => ListAll("") with { EndConnection = true },
            _ => ListAll("") with { WaitForDisconnect = true, Reached = reached, Response = "" }
        };
        await using var server = new ScriptedImapServer(new Session(Login(failing)),
            new Session(Login(ListAll("* LIST (\\Noselect) \"/\" Labels\r\n* LIST () \"/\" Labels/Recovered\r\n"), Logout())));
        using var mailbox = new MailKitMailbox(server.Settings(folderAccessMode: FolderAccessMode.AllExcept));
        using var cancellation = new CancellationTokenSource();
        var call = mailbox.ListFoldersAsync(cancellation.Token);
        if (failure == "cancelled")
        {
            await reached.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
        }
        else await Error(failure == "rejected" ? MailboxError.ProtocolFailure : MailboxError.BridgeUnavailable, () => call);
        Assert.Contains(await mailbox.ListFoldersAsync(CancellationToken.None), f => f.Name == "Labels/Recovered");
        await server.VerifyAsync();
        Assert.Equal(2, server.AcceptedConnections);
    }

    private static async Task Error(MailboxError expected, Func<Task> action)
    {
        var exception = await Assert.ThrowsAsync<MailboxException>(action);
        Assert.Equal(expected, exception.Error);
        Assert.Null(exception.InnerException);
    }
}
