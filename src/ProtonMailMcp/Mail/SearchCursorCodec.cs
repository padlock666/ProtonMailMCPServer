using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using ProtonMailMcp.Contracts;

namespace ProtonMailMcp.Mail;

// Only the random key survives between calls. No mailbox data or issued-token registry is retained.
internal sealed class SearchCursorCodec
{
    internal const int EncodedLength = 98;
    private readonly byte[] key;

    public SearchCursorCodec() : this(RandomNumberGenerator.GetBytes(32)) { }
    internal SearchCursorCodec(byte[] key)
    {
        if (key.Length != 32) throw new ArgumentException("Invalid cursor key length.", nameof(key));
        this.key = key.ToArray();
    }

    internal readonly record struct Position(uint UidValidity, uint BeforeUid);

    internal string Encode(SearchRequest request, uint uidValidity, uint beforeUid)
    {
        if (uidValidity == 0 || beforeUid == 0) throw Invalid();
        var bytes = new byte[73];
        bytes[0] = 1;
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(1), uidValidity);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(5), beforeUid);
        Binding(request).CopyTo(bytes, 9);
        Tag(bytes.AsSpan(0, 41)).CopyTo(bytes, 41);
        return EncodeBytes(bytes);
    }

    internal Position Decode(string cursor, SearchRequest request)
    {
        if (cursor.Length != EncodedLength || cursor.Any(c =>
            !(c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_')))
            throw Invalid();
        byte[] bytes;
        try { bytes = Convert.FromBase64String(cursor.Replace('-', '+').Replace('_', '/') + "=="); }
        catch (FormatException) { throw Invalid(); }
        if (bytes.Length != 73 || EncodeBytes(bytes) != cursor || bytes[0] != 1 ||
            !CryptographicOperations.FixedTimeEquals(Tag(bytes.AsSpan(0, 41)), bytes.AsSpan(41)) ||
            !CryptographicOperations.FixedTimeEquals(Binding(request), bytes.AsSpan(9, 32)))
            throw Invalid();
        var validity = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(1));
        var boundary = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(5));
        if (validity == 0 || boundary == 0) throw Invalid();
        return new(validity, boundary);
    }

    private byte[] Binding(SearchRequest request)
    {
        object?[] values = [NormalizeFolder(request.Folder), request.Sender, request.Recipient, request.Subject,
            request.SentStart?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            request.SentEnd?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), request.Unread];
        var json = JsonSerializer.SerializeToUtf8Bytes(values, JsonSerializerOptions.Web);
        return Authenticate("protonmcp-search-binding-v1\0"u8, json);
    }

    private byte[] Tag(ReadOnlySpan<byte> payload) => Authenticate("protonmcp-search-cursor-v1\0"u8, payload);

    private byte[] Authenticate(ReadOnlySpan<byte> domain, ReadOnlySpan<byte> payload)
    {
        var input = new byte[domain.Length + payload.Length];
        domain.CopyTo(input);
        payload.CopyTo(input.AsSpan(domain.Length));
        return HMACSHA256.HashData(key, input);
    }

    internal static string NormalizeFolder(string folder) =>
        string.Equals(folder, "INBOX", StringComparison.OrdinalIgnoreCase) ? "INBOX" : folder;
    private static string EncodeBytes(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static MailboxException Invalid() => new(MailboxError.InvalidInput);
}
