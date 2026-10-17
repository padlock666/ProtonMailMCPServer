using MailKit;
using MailKit.Net.Imap;

namespace ProtonMailMcp.Mail;

internal sealed class BodyLimitException : IOException;

// MailKit allocates body backing streams through this extension point before reading literals.
internal sealed class BoundedImapClient : ImapClient
{
    protected override ImapFolder CreateImapFolder(ImapFolderConstructorArgs args) => new BoundedFolder(args);

    private sealed class BoundedFolder(ImapFolderConstructorArgs args) : ImapFolder(args)
    {
        protected override Stream CreateStream(UniqueId? uid, string section, int offset, int length)
        {
            if (length < 0 || length > MimeText.MaxTransferBytes) throw new BodyLimitException();
            return new BoundedMemoryStream();
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
