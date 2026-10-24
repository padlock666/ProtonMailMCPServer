using MailKit;
using MailKit.Net.Imap;

namespace ProtonMailMcp.Mail;

internal sealed class BodyLimitException : IOException;

// MailKit allocates body backing streams through this extension point before reading literals.
internal sealed class BoundedImapClient : ImapClient
{
    private uint? attachmentUid;
    private string? attachmentSection;
    private bool allocated;
    internal void ExpectAttachment(uint uid, string section)
    {
        attachmentUid = uid;
        attachmentSection = section;
    }
    internal void RequireNoAttachmentLiteral()
    {
        // MailKit can discard a stream whose UID appears after the literal and does not
        // match the request, without calling CommitStream. Never reinterpret that as absence.
        if (allocated) throw new ProtonMailMcp.Contracts.MailboxException(ProtonMailMcp.Contracts.MailboxError.ProtocolFailure);
    }
    protected override ImapFolder CreateImapFolder(ImapFolderConstructorArgs args) => new BoundedFolder(args, this);

    private sealed class BoundedFolder(ImapFolderConstructorArgs args, BoundedImapClient client) : ImapFolder(args)
    {
        protected override Stream CreateStream(UniqueId? uid, string section, int offset, int length)
        {
            if (client.attachmentSection is { } expected)
            {
                if (offset != 0 || section != expected || client.allocated ||
                    uid.HasValue && uid.Value.Id != client.attachmentUid)
                    throw new ProtonMailMcp.Contracts.MailboxException(ProtonMailMcp.Contracts.MailboxError.ProtocolFailure);
                if (length < 0 || length > PrivateDownloadStore.MaxEncodedBytes)
                    throw new ProtonMailMcp.Contracts.MailboxException(ProtonMailMcp.Contracts.MailboxError.AttachmentTooLarge);
                client.allocated = true;
                // Fixed capacity: MemoryStream growth cannot double the transfer allowance.
                var stream = new MemoryStream(new byte[length], 0, length, writable: true, publiclyVisible: false);
                stream.SetLength(0);
                return stream;
            }
            if (length < 0 || length > MimeText.MaxTransferBytes) throw new BodyLimitException();
            return new BoundedMemoryStream();
        }

        protected override Stream CommitStream(Stream stream, UniqueId uid, string section, int offset, int length)
        {
            if (client.attachmentSection is { } expected &&
                (uid.Id != client.attachmentUid || section != expected || offset != 0 || stream.Length != length))
                throw new ProtonMailMcp.Contracts.MailboxException(ProtonMailMcp.Contracts.MailboxError.ProtocolFailure);
            return base.CommitStream(stream, uid, section, offset, length);
        }
    }
}

internal sealed class BoundedMemoryStream : MemoryStream
{
    private void Check(int count)
    {
        if (count > MimeText.MaxTransferBytes - Position) throw new BodyLimitException();
    }
    public override void Write(byte[] buffer, int offset, int count) { Check(count); base.Write(buffer, offset, count); }
    public override void Write(ReadOnlySpan<byte> buffer) { Check(buffer.Length); base.Write(buffer); }
    public override void WriteByte(byte value) { Check(1); base.WriteByte(value); }
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Write(buffer, offset, count);
        return Task.CompletedTask;
    }
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Write(buffer.Span);
        return ValueTask.CompletedTask;
    }
    public override void SetLength(long value)
    {
        if (value > MimeText.MaxTransferBytes) throw new BodyLimitException();
        base.SetLength(value);
    }
}
