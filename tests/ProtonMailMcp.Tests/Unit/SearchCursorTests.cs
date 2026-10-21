using System.Buffers.Binary;
using System.Security.Cryptography;
using ProtonMailMcp.Contracts;
using ProtonMailMcp.Mail;

namespace ProtonMailMcp.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class SearchCursorTests
{
    private static readonly byte[] Key = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();

    [Theory]
    [InlineData(1u, 1u)]
    [InlineData(uint.MaxValue, uint.MaxValue)]
    public void RoundTripPreservesIdentifiersAndAllowsPageSizeAndInboxCaseChanges(uint validity, uint boundary)
    {
        var codec = new SearchCursorCodec(Key);
        var request = new SearchRequest("inbox", "s\"\\é", "r", "subject", new(2026, 1, 1), new(2026, 2, 1), false);
        var token = codec.Encode(request, validity, boundary);
        Assert.Equal(98, token.Length);
        Assert.Matches("^[A-Za-z0-9_-]+$", token);
        Assert.Equal(new(validity, boundary), codec.Decode(token, request with { Folder = "INBOX", Limit = 1, Cursor = token }));
    }

    [Fact]
    public void EveryFilterIsBoundExactlyAndOtherInstancesRejectTokens()
    {
        var codec = new SearchCursorCodec(Key);
        var request = new SearchRequest("Work", "s", "r", "q", new(2026, 1, 1), new(2026, 2, 1), true);
        var token = codec.Encode(request, 7, 42);
        SearchRequest[] changed = [request with { Folder = "work" }, request with { Sender = "S" },
            request with { Recipient = null }, request with { Subject = "q " },
            request with { SentStart = new(2026, 1, 2) }, request with { SentEnd = null },
            request with { Unread = false }, request with { Unread = null }];
        foreach (var other in changed) Invalid(() => codec.Decode(token, other));
        Invalid(() => new SearchCursorCodec().Decode(token, request));
        var empty = new SearchRequest("INBOX");
        Assert.Equal(new(7, 42), codec.Decode(codec.Encode(empty, 7, 42), empty with { Cursor = null, Limit = 50 }));
        Invalid(() => codec.Decode(codec.Encode(empty, 7, 42), empty with { Unread = false }));
    }

    [Fact]
    public void RejectsMalformedTamperedNoncanonicalAndSignedInvalidEnvelopes()
    {
        var codec = new SearchCursorCodec(Key);
        var request = new SearchRequest("INBOX");
        var token = codec.Encode(request, 7, 42);
        foreach (var malformed in new[] { "", " ", token[..97], token + "=", new string('A', 99),
            "+" + token[1..], token[..97] + "!", new string('A', 98) })
            Invalid(() => codec.Decode(malformed, request));
        var bytes = Convert.FromBase64String(token.Replace('-', '+').Replace('_', '/') + "==");
        foreach (var index in new[] { 0, 1, 5, 9, 41, 72 })
        {
            var altered = bytes.ToArray(); altered[index] ^= 1;
            Invalid(() => codec.Decode(Encode(altered), request));
        }
        // The last base64 symbol contains only two payload bits; reject alternate padding bits.
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";
        var noncanonical = token[..97] + alphabet[alphabet.IndexOf(token[^1]) + 1];
        Invalid(() => codec.Decode(noncanonical, request));
        foreach (var field in new[] { "version", "validity", "boundary" })
        {
            var altered = bytes.ToArray();
            if (field == "version") altered[0] = 2;
            else BinaryPrimitives.WriteUInt32BigEndian(altered.AsSpan(field == "validity" ? 1 : 5), 0);
            var domain = "protonmcp-search-cursor-v1\0"u8.ToArray();
            HMACSHA256.HashData(Key, domain.Concat(altered.Take(41)).ToArray()).CopyTo(altered, 41);
            Invalid(() => codec.Decode(Encode(altered), request));
        }
    }

    private static string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static void Invalid(Action action) =>
        Assert.Equal(MailboxError.InvalidInput, Assert.Throws<MailboxException>(action).Error);
}
