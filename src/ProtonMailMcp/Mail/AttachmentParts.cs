using System.Globalization;
using System.Security.Cryptography;
using MailKit;
using ProtonMailMcp.Contracts;

namespace ProtonMailMcp.Mail;

internal static class AttachmentParts
{
    internal static bool ValidId(string? value) => value is { Length: > 0 and <= 128 } &&
        value.Split('.') is { Length: <= 16 } parts && parts.All(p => p.Length > 0 && p[0] != '0' &&
            p.All(c => c is >= '0' and <= '9') && uint.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out _));

    internal static Dictionary<string, BodyPart> Map(BodyPart? root, CancellationToken token)
    {
        var result = new Dictionary<string, BodyPart>(StringComparer.Ordinal);
        var nodes = 0;
        void Walk(BodyPart? part, int depth)
        {
            token.ThrowIfCancellationRequested();
            if (part is null) return;
            if (++nodes > 1024 || depth > 16) throw new MailboxException(MailboxError.AttachmentUnsupported);
            var id = string.IsNullOrEmpty(part.PartSpecifier) ? "1" : part.PartSpecifier;
            if (part is BodyPartMessage || part is BodyPartMultipart { ContentDisposition.IsAttachment: true })
            {
                if (ValidId(id) && !result.TryAdd(id, part)) throw new MailboxException(MailboxError.ProtocolFailure);
                return;
            }
            if (part is BodyPartMultipart multipart)
                foreach (var child in multipart.BodyParts) Walk(child, depth + 1);
            else if (part is BodyPartBasic { IsAttachment: true } && ValidId(id) && !result.TryAdd(id, part))
                throw new MailboxException(MailboxError.ProtocolFailure);
        }
        Walk(root, 0);
        return result;
    }

    internal static string Encoding(BodyPartBasic part) => part.ContentTransferEncoding?.ToLowerInvariant() ?? "7bit";
    internal static void RequireEncoding(string encoding)
    {
        if (encoding is not ("base64" or "quoted-printable" or "7bit" or "8bit" or "binary"))
            throw new MailboxException(MailboxError.AttachmentUnsupported);
    }

    // The source is the already bounded encoded literal. No charset or document parsing.
    internal static async Task<(long Length, string Hash)> DecodeAsync(Stream source, Stream destination,
        string encoding, CancellationToken token)
    {
        RequireEncoding(encoding);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var decoder = new TransferDecoder(source, encoding, token);
        var buffer = new byte[8192];
        long length = 0;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var count = 0;
            for (; count < buffer.Length; count++)
            {
                var next = decoder.ReadByte();
                if (next < 0) break;
                buffer[count] = (byte)next;
            }
            if (count == 0) break;
            if (count > PrivateDownloadStore.MaxFileBytes - length)
                throw new MailboxException(MailboxError.AttachmentTooLarge);
            await destination.WriteAsync(buffer.AsMemory(0, count), token);
            hash.AppendData(buffer, 0, count);
            length += count;
        }
        return (length, Convert.ToHexStringLower(hash.GetHashAndReset()));
    }

    // Narrow strict MIME transfer decoder: bounded output chunks, canonical base64 padding,
    // and complete quoted-printable escapes. Unlike text conversion, binary bytes stay exact.
    private sealed class TransferDecoder(Stream source, string encoding, CancellationToken token)
    {
        private readonly byte[] pending = new byte[3];
        private int position, count;
        private bool ended;
        public int ReadByte()
        {
            if (position < count) return pending[position++];
            var next = InputByte();
            if (encoding == "base64")
            {
                while (next is ' ' or '\t' or '\r' or '\n') next = InputByte();
                if (next < 0) return -1;
                if (ended) Malformed();
                var a = Base64(next);
                var b = Base64(NonWhitespace());
                var c = NonWhitespace();
                var d = NonWhitespace();
                if (a < 0 || b < 0) Malformed();
                pending[0] = (byte)((a << 2) | (b >> 4));
                count = 1;
                if (c == '=')
                {
                    if (d != '=' || (b & 15) != 0) Malformed();
                    ended = true;
                }
                else
                {
                    var cv = Base64(c);
                    if (cv < 0) Malformed();
                    pending[1] = (byte)((b << 4) | (cv >> 2));
                    count = 2;
                    if (d == '=')
                    {
                        if ((cv & 3) != 0) Malformed();
                        ended = true;
                    }
                    else
                    {
                        var dv = Base64(d);
                        if (dv < 0) Malformed();
                        pending[2] = (byte)((cv << 6) | dv);
                        count = 3;
                    }
                }
                position = 1;
                return pending[0];
            }
            if (encoding == "7bit" && next > 127) Malformed();
            if (encoding != "quoted-printable") return next;
            while (next == '=')
            {
                var first = InputByte();
                var second = InputByte();
                if (first == '\r' && second == '\n') { next = InputByte(); continue; }
                if (Hex(first) < 0 || Hex(second) < 0) Malformed();
                return Hex(first) * 16 + Hex(second);
            }
            if (next >= 0 && next is not ('\t' or '\r' or '\n' or >= 32 and <= 126)) Malformed();
            return next;
        }
        private int consumed;
        private int InputByte()
        {
            if ((consumed++ & 8191) == 0) token.ThrowIfCancellationRequested();
            return source.ReadByte();
        }
        private int NonWhitespace()
        {
            int next;
            do { next = InputByte(); } while (next is ' ' or '\t' or '\r' or '\n');
            return next;
        }
        private static int Base64(int c) => c switch
        {
            >= 'A' and <= 'Z' => c - 'A', >= 'a' and <= 'z' => c - 'a' + 26,
            >= '0' and <= '9' => c - '0' + 52, '+' => 62, '/' => 63, _ => -1
        };
        private static int Hex(int c) => c switch
        {
            >= '0' and <= '9' => c - '0', >= 'A' and <= 'F' => c - 'A' + 10,
            >= 'a' and <= 'f' => c - 'a' + 10, _ => -1
        };
        private static void Malformed() => throw new MailboxException(MailboxError.AttachmentMalformed);
    }
}
