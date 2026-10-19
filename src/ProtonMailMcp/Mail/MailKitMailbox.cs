using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using ProtonMailMcp.Configuration;
using ProtonMailMcp.Contracts;
using AppSummary = ProtonMailMcp.Contracts.MessageSummary;

namespace ProtonMailMcp.Mail;

internal sealed class MailKitMailbox(BridgeSettings settings) : IReadOnlyMailbox, IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private const int MaxFolderResults = 4096;
    // Internal test seam for deterministic short deadline fixtures; production always uses 30 seconds.
    internal TimeSpan OperationDeadline { get; init; } = TimeSpan.FromSeconds(30);
    internal TimeProvider DeadlineClock { get; init; } = TimeProvider.System;
    private const MessageSummaryItems SummaryItems = MessageSummaryItems.UniqueId | MessageSummaryItems.Envelope |
        MessageSummaryItems.InternalDate | MessageSummaryItems.Flags | MessageSummaryItems.Size;

    public Task<IReadOnlyList<FolderResult>> ListFoldersAsync(CancellationToken cancellationToken)
    {
        if (settings.FolderAccessMode == FolderAccessMode.Allowlist && settings.AllowedFolders.Count == 0)
            throw new MailboxException(MailboxError.DeniedFolder);
        return ExecuteAsync<IReadOnlyList<FolderResult>>(async (client, token) =>
        {
            var folders = new List<FolderResult>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            void AddFolder(IMailFolder folder)
            {
                if (folder.Exists && !folder.Attributes.HasFlag(FolderAttributes.NoSelect) &&
                    !string.IsNullOrWhiteSpace(folder.FullName) && folder.FullName.Length <= 1024 &&
                    !folder.FullName.Any(char.IsControl) && settings.IsFolderAllowed(folder.FullName) &&
                    seen.Add(string.Equals(folder.FullName, "INBOX", StringComparison.OrdinalIgnoreCase) ? "INBOX" : folder.FullName))
                {
                    if (folders.Count == MaxFolderResults) throw new MailboxException(MailboxError.ProtocolFailure);
                    folders.Add(new(folder.FullName));
                }
            }

            if (settings.FolderAccessMode == FolderAccessMode.AllExcept)
            {
                AddFolder(client.Inbox);
                var namespaces = client.PersonalNamespaces.Concat(client.SharedNamespaces).Concat(client.OtherNamespaces)
                    .DistinctBy(item => (item.Path, item.DirectorySeparator));
                foreach (var ns in namespaces)
                {
                    var discovered = await client.GetFoldersAsync(ns, StatusItems.None, subscribedOnly: false, token);
                    foreach (var folder in discovered)
                    {
                        token.ThrowIfCancellationRequested();
                        AddFolder(folder);
                    }
                }
            }
            else
            {
                foreach (var name in settings.AllowedFolders)
                {
                    token.ThrowIfCancellationRequested();
                    try { AddFolder(await client.GetFolderAsync(name, token)); }
                    catch (FolderNotFoundException) { }
                }
            }
            return folders.AsReadOnly();
        }, cancellationToken);
    }

    public Task<SearchResult> SearchAsync(SearchRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateFolder(request.Folder);
        var query = BuildQuery(request);
        return ExecuteAsync(async (client, token) =>
        {
            var folder = await OpenAsync(client, request.Folder, token);
            var matches = await folder.SearchAsync(query, token);
            var uids = matches.OrderByDescending(uid => uid.Id).Take(request.Limit).ToArray();
            if (uids.Length == 0) return new SearchResult([], false);
            var summaries = await folder.FetchAsync(uids, SummaryItems, token);
            var requested = uids.Select(uid => uid.Id).ToHashSet();
            var results = summaries.Where(summary => requested.Contains(summary.UniqueId.Id))
                .OrderByDescending(summary => summary.UniqueId.Id)
                .Take(request.Limit).Select(summary => MapSummary(folder, summary)).ToArray();
            return new SearchResult(Array.AsReadOnly(results), matches.Count > request.Limit);
        }, cancellationToken);
    }

    public Task<MessageResult> GetMessageAsync(MessageReference reference, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ValidateFolder(reference.Folder);
        if (reference.Uid == 0 || reference.UidValidity == 0) throw new MailboxException(MailboxError.InvalidInput);
        return ExecuteAsync(async (client, token) =>
        {
            var folder = await OpenAsync(client, reference.Folder, token);
            if (folder.UidValidity != reference.UidValidity) throw new MailboxException(MailboxError.StaleReference);
            var uid = new UniqueId(reference.UidValidity, reference.Uid);
            var summaries = await folder.FetchAsync(new[] { uid }, SummaryItems | MessageSummaryItems.BodyStructure, token);
            var summary = summaries.SingleOrDefault(item => item.UniqueId.Id == uid.Id)
                ?? throw new MailboxException(MailboxError.MessageNotFound);
            var attachments = summary.Attachments.Take(101).ToArray();
            var metadata = attachments.Take(100).Select(part => new AttachmentMetadata(
                MimeText.Sanitize(part.FileName, 1024), MimeText.Sanitize(part.ContentType.MimeType, 256)!, part.Octets)).ToArray();
            var mapped = MapSummary(folder, summary);
            MessageResult Result(string? text, ContentStatus status, bool truncated = false) =>
                new(mapped, text, status, truncated, Array.AsReadOnly(metadata), attachments.Length > 100);
            var body = MimeText.Select(summary);
            if (body is null) return Result(null, summary.Body is null ? ContentStatus.Missing : ContentStatus.Unsupported);
            if (body.Octets > MimeText.MaxTransferBytes) return Result(null, ContentStatus.Oversized);
            try
            {
                // Explicit section only, never the full MIME message. MailKit emits BODY.PEEK[section].
                using var stream = await folder.GetStreamAsync(uid, string.IsNullOrEmpty(body.PartSpecifier) ? "TEXT" : body.PartSpecifier, token);
                var decoded = await MimeText.DecodeAsync(body, stream, token);
                return Result(decoded.Text, decoded.Status, decoded.Truncated);
            }
            catch (BodyLimitException) { return Result(null, ContentStatus.Oversized); }
            catch (MessageNotFoundException) { throw new MailboxException(MailboxError.MessageNotFound); }
        }, cancellationToken);
    }

    private void ValidateFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || folder.Length > 1024 || folder.Any(char.IsControl))
            throw new MailboxException(MailboxError.InvalidInput);
        if (!settings.IsFolderAllowed(folder)) throw new MailboxException(MailboxError.DeniedFolder);
    }

    internal static SearchQuery BuildQuery(SearchRequest request)
    {
        if (request.Limit is < 1 or > 50 ||
            request.SentStart.HasValue && request.SentEnd.HasValue && request.SentStart >= request.SentEnd)
            throw new MailboxException(MailboxError.InvalidInput);
        var query = SearchQuery.All;
        foreach (var value in new[] { request.Sender, request.Recipient, request.Subject })
            if (value is not null && (string.IsNullOrWhiteSpace(value) || value.Length > 1024 || value.Any(char.IsControl)))
                throw new MailboxException(MailboxError.InvalidInput);
        if (request.Sender is not null) query = query.And(SearchQuery.FromContains(request.Sender));
        if (request.Recipient is not null) query = query.And(SearchQuery.ToContains(request.Recipient).Or(SearchQuery.CcContains(request.Recipient)));
        if (request.Subject is not null) query = query.And(SearchQuery.SubjectContains(request.Subject));
        // Search the sender Date header via IMAP sent-date keys; never fall back to INTERNALDATE.
        if (request.SentStart is { } start) query = query.And(SearchQuery.SentSince(start.ToDateTime(TimeOnly.MinValue)));
        if (request.SentEnd is { } end) query = query.And(SearchQuery.SentBefore(end.ToDateTime(TimeOnly.MinValue)));
        if (request.Unread is { } unread) query = query.And(unread ? SearchQuery.NotSeen : SearchQuery.Seen);
        return query;
    }

    private async Task<IMailFolder> OpenAsync(ImapClient client, string name, CancellationToken token)
    {
        var folder = await client.GetFolderAsync(name, token);
        if (!folder.Exists || folder.Attributes.HasFlag(FolderAttributes.NoSelect))
            throw new MailboxException(MailboxError.FolderUnavailable);
        // Recheck the resolved name as well as the caller's name.
        if (!settings.IsFolderAllowed(folder.FullName)) throw new MailboxException(MailboxError.DeniedFolder);
        await folder.OpenAsync(FolderAccess.ReadOnly, token);
        if (folder.Access != FolderAccess.ReadOnly) throw new MailboxException(MailboxError.ProtocolFailure);
        return folder;
    }

    private static AppSummary MapSummary(IMailFolder folder, IMessageSummary summary) => new(
        new(folder.FullName, folder.UidValidity, summary.UniqueId.Id),
        MimeText.Sanitize(summary.Envelope?.From?.ToString(), 4096),
        MimeText.Sanitize(summary.Envelope?.To?.ToString(), 4096),
        MimeText.Sanitize(summary.Envelope?.Cc?.ToString(), 4096),
        MimeText.Sanitize(summary.Envelope?.Subject, 4096), summary.Envelope?.Date,
        summary.InternalDate, summary.Flags.HasValue ? summary.Flags.Value.HasFlag(MessageFlags.Seen) : null, summary.Size);

    private async Task<T> ExecuteAsync<T>(Func<ImapClient, CancellationToken, Task<T>> operation, CancellationToken callerToken)
    {
        using var timeout = new CancellationTokenSource(OperationDeadline, DeadlineClock);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(callerToken, timeout.Token);
        var token = deadline.Token;
        var entered = false;
        try
        {
            await gate.WaitAsync(token);
            entered = true;
            using var client = new BoundedImapClient();
            client.ServerCertificateValidationCallback = (_, certificate, _, _) =>
            {
                if (certificate is null) return false;
                using var peer = new X509Certificate2(certificate);
                var now = DateTime.UtcNow;
                return now >= peer.NotBefore.ToUniversalTime() && now <= peer.NotAfter.ToUniversalTime() &&
                    CryptographicOperations.FixedTimeEquals(peer.RawData, settings.CertificateDer.Span);
            };
            // No production protocol logger. Mandatory TLS must complete before authentication.
            await client.ConnectAsync(settings.Host.ToString(), settings.Port,
                settings.TlsMode == BridgeTlsMode.StartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.SslOnConnect, token);
            if (!client.IsSecure) throw new MailboxException(MailboxError.TlsFailed);
            await client.AuthenticateAsync(settings.Username, settings.Password, token);
            var result = await operation(client, token);
            // Only successful, synchronized sessions log out. Errors/cancellation dispose immediately.
            // No folder Close call; LOGOUT cannot expunge a selected read-only folder.
            await client.DisconnectAsync(true, token);
            // MailKit may absorb cancellation during LOGOUT; enforce the application deadline too.
            token.ThrowIfCancellationRequested();
            return result;
        }
        catch (Exception) when (callerToken.IsCancellationRequested) { throw new OperationCanceledException(callerToken); }
        catch (Exception) when (token.IsCancellationRequested) { throw new MailboxException(MailboxError.Timeout); }
        catch (MailboxException) { throw; }
        catch (SslHandshakeException) { throw new MailboxException(MailboxError.TlsFailed); }
        catch (System.Security.Authentication.AuthenticationException) { throw new MailboxException(MailboxError.TlsFailed); }
        catch (MailKit.Security.AuthenticationException) { throw new MailboxException(MailboxError.AuthenticationFailed); }
        catch (NotSupportedException) { throw new MailboxException(MailboxError.TlsFailed); }
        catch (FolderNotFoundException) { throw new MailboxException(MailboxError.FolderUnavailable); }
        catch (FolderNotOpenException) { throw new MailboxException(MailboxError.ProtocolFailure); }
        catch (ImapCommandException) { throw new MailboxException(MailboxError.ProtocolFailure); }
        catch (ImapProtocolException) { throw new MailboxException(MailboxError.BridgeUnavailable); }
        catch (IOException) { throw new MailboxException(MailboxError.BridgeUnavailable); }
        catch (System.Net.Sockets.SocketException) { throw new MailboxException(MailboxError.BridgeUnavailable); }
        catch (Exception) { throw new MailboxException(MailboxError.ProtocolFailure); }
        finally { if (entered) gate.Release(); }
    }

    public void Dispose() => gate.Dispose();
}
