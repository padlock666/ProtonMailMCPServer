namespace ProtonMailMcp.Contracts;

internal sealed record SearchRequest(string Folder, string? Sender = null, string? Recipient = null,
    string? Subject = null, DateOnly? SentStart = null, DateOnly? SentEnd = null,
    bool? Unread = null, int Limit = 20, string? Cursor = null);
internal sealed record MessageReference(string Folder, uint UidValidity, uint Uid);
internal sealed record FolderResult(string Name);
internal sealed record MessageSummary(MessageReference Reference, string? From, string? To, string? Cc,
    string? Subject, DateTimeOffset? SenderDate, DateTimeOffset? InternalDate, bool? Seen, uint? Size);
internal sealed record SearchResult(IReadOnlyList<MessageSummary> Messages, bool Truncated, string? NextCursor = null);
internal sealed record AttachmentMetadata(string? Filename, string MimeType, uint Size);
internal enum ContentStatus { Available, Missing, Unsupported, Oversized, Malformed }
internal sealed record MessageResult(MessageSummary Summary, string? Text, ContentStatus ContentStatus,
    bool Truncated, IReadOnlyList<AttachmentMetadata> Attachments, bool AttachmentsTruncated);
internal enum MailboxError
{
    InvalidInput, DeniedFolder, FolderUnavailable, BridgeUnavailable, AuthenticationFailed,
    TlsFailed, StaleReference, MessageNotFound, Timeout, ProtocolFailure
}
// Fixed code only: never retain a raw exception, server response, or caller input.
internal sealed class MailboxException(MailboxError error) : Exception(error.ToString())
{
    public MailboxError Error { get; } = error;
}
