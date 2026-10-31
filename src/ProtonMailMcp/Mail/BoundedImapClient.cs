using MailKit;
using MailKit.Net.Imap;
using ProtonMailMcp.Contracts;

namespace ProtonMailMcp.Mail;

internal sealed class BodyLimitException : IOException;

// MailKit allocates body backing streams through this extension point before reading literals.
internal sealed class BoundedImapClient : ImapClient
{
    private uint? expectedUid;
    private string? expectedSection;
    private bool attachment;
    private bool allocated;
    internal void ExpectBody(uint uid, string section, bool isAttachment = false)
    {
        expectedUid = uid;
        expectedSection = section;
        attachment = isAttachment;
    }
    internal void RequireNoPayloadLiteral()
    {
        // MailKit can discard a stream whose UID appears after the literal and does not
        // match the request, without calling CommitStream. Never reinterpret that as absence.
        if (allocated) throw new MailboxException(MailboxError.ProtocolFailure);
    }
    protected override ImapFolder CreateImapFolder(ImapFolderConstructorArgs args) => new BoundedFolder(args, this);

    private sealed class BoundedFolder(ImapFolderConstructorArgs args, BoundedImapClient client) : ImapFolder(args)
    {
        protected override Stream CreateStream(UniqueId? uid, string section, int offset, int length)
        {
            if (client.expectedSection is not { } expected || offset != 0 || section != expected || client.allocated ||
                uid.HasValue && uid.Value.Id != client.expectedUid)
                throw new MailboxException(MailboxError.ProtocolFailure);
            if (length < 0 || length > (client.attachment ? PrivateDownloadStore.MaxEncodedBytes : MimeText.MaxTransferBytes))
            {
                if (client.attachment) throw new MailboxException(MailboxError.AttachmentTooLarge);
                throw new BodyLimitException();
            }
            client.allocated = true;
            // One fixed-capacity literal for the requested UID/section; duplicates cannot multiply the allowance.
            var stream = new MemoryStream(new byte[length], 0, length, writable: true, publiclyVisible: false);
            stream.SetLength(0);
            return stream;
        }

        protected override Stream CommitStream(Stream stream, UniqueId uid, string section, int offset, int length)
        {
            if (uid.Id != client.expectedUid || section != client.expectedSection || offset != 0 || stream.Length != length)
                throw new MailboxException(MailboxError.ProtocolFailure);
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
