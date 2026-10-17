using System.Text;
using MailKit;
using MimeKit;
using ProtonMailMcp.Contracts;
using ProtonMailMcp.Mail;
using ProtonMailMcp.Tests.Fixtures;

namespace ProtonMailMcp.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class MimeAndMailboxValidationTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(51)]
    public void RejectsInvalidLimits(int limit) => Assert.Equal(MailboxError.InvalidInput,
        Assert.Throws<MailboxException>(() => MailKitMailbox.BuildQuery(new("INBOX", Limit: limit))).Error);

    [Fact]
    public void RejectsEqualAndReversedDateRangesAndUnsafeFilters()
    {
        var day = new DateOnly(2026, 10, 17);
        foreach (var end in new[] { day, day.AddDays(-1) })
            Assert.Throws<MailboxException>(() => MailKitMailbox.BuildQuery(new("INBOX", ReceivedStart: day, ReceivedEnd: end)));
        foreach (var value in new[] { "", " ", "x\r\nUID STORE", new string('x', 1025) })
        {
            Assert.Throws<MailboxException>(() => MailKitMailbox.BuildQuery(new("INBOX", Sender: value)));
            Assert.Throws<MailboxException>(() => MailKitMailbox.BuildQuery(new("INBOX", Recipient: value)));
            Assert.Throws<MailboxException>(() => MailKitMailbox.BuildQuery(new("INBOX", Subject: value)));
        }
    }

    [Fact]
    public async Task AllowlistIsExactWithInboxException()
    {
        using var fixture = new SyntheticConfiguration();
        // This unit test loads synthetic settings; it does not create a socket.
        var settings = await ProtonMailMcp.Configuration.ConfigurationLoader.LoadAsync(fixture.ConfigPath, CancellationToken.None);
        Assert.True(settings.IsFolderAllowed("inbox"));
        Assert.True(settings.IsFolderAllowed("Approved"));
        Assert.False(settings.IsFolderAllowed("approved"));
        Assert.False(settings.IsFolderAllowed("INBOX/child"));
    }

    [Theory]
    [InlineData("plain", "hello", "7bit", "UTF-8", "hello")]
    [InlineData("plain", "aGVsbG8=", "base64", "UTF-8", "hello")]
    [InlineData("plain", "caf=C3=A9", "quoted-printable", "UTF-8", "café")]
    [InlineData("html", "<p>Hello &amp; goodbye</p><style>hidden</style><script>hidden</script>", "7bit", "UTF-8", "Hello & goodbye")]
    public async Task DecodesOnlyTextAndConvertsHtml(string subtype, string wire, string transfer, string charset, string expected)
    {
        var part = Part(subtype, transfer, charset);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(wire));
        var result = await MimeText.DecodeAsync(part, stream, CancellationToken.None);
        Assert.Equal(ContentStatus.Available, result.Status);
        Assert.Equal(expected, result.Text!.Trim());
        Assert.False(result.Truncated);
    }

    [Theory]
    [InlineData("unknown-encoding", "UTF-8")]
    [InlineData("7bit", "unknown-charset")]
    [InlineData("7bit", "UTF-8")]
    public async Task InvalidEncodingReturnsExplicitMalformedStatus(string transfer, string charset)
    {
        using var stream = new MemoryStream([0xff]);
        var result = await MimeText.DecodeAsync(Part("plain", transfer, charset), stream, CancellationToken.None);
        Assert.Null(result.Text);
        Assert.Equal(ContentStatus.Malformed, result.Status);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(32000, false)]
    [InlineData(32001, true)]
    public async Task EmptyAndOutputBoundaryHaveExplicitSuccessfulStatus(int length, bool truncated)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(new string('x', length)));
        var result = await MimeText.DecodeAsync(Part("plain", "7bit", "UTF-8"), stream, CancellationToken.None);
        Assert.Equal(ContentStatus.Available, result.Status);
        Assert.Equal(Math.Min(length, 32000), result.Text!.Length);
        Assert.Equal(truncated, result.Truncated);
    }

    [Fact]
    public async Task DecodingIsBoundedAndCancellable()
    {
        using var stream = new MemoryStream(new byte[MimeText.MaxTransferBytes + 1]);
        var result = await MimeText.DecodeAsync(Part("plain", "8bit", "UTF-8"), stream, CancellationToken.None);
        Assert.Equal(ContentStatus.Oversized, result.Status);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => MimeText.DecodeAsync(Part("plain", "7bit", "UTF-8"), stream, cancellation.Token));
    }

    [Fact]
    public void BoundedBackingStreamRejectsAllWritePathsBeforeGrowth()
    {
        using var stream = new BoundedMemoryStream();
        stream.SetLength(MimeText.MaxTransferBytes);
        stream.Position = stream.Length;
        Assert.Throws<BodyLimitException>(() => stream.WriteByte(1));
        Assert.Throws<BodyLimitException>(() => stream.Write(new byte[1], 0, 1));
        Assert.Throws<BodyLimitException>(() => stream.Write(new ReadOnlySpan<byte>(new byte[1])));
        Assert.Throws<BodyLimitException>(() => stream.SetLength(MimeText.MaxTransferBytes + 1));
        Assert.Equal(MimeText.MaxTransferBytes, stream.Length);
    }

    [Fact]
    public void SanitizesControlsAndAvoidsSplittingSurrogatePairs()
    {
        Assert.Equal("ab", MimeText.Sanitize("a\0\u001bb", 20));
        Assert.Equal("a\nb", MimeText.Sanitize("a\nb", 20, true));
        Assert.Equal("x", MimeText.Sanitize("x😀", 2));
    }

    [Fact]
    public void MainBodySelectionRespectsAlternativesRelatedRootsAndAttachedMessages()
    {
        var plain = Part("plain", "7bit", "UTF-8");
        var html = Part("html", "7bit", "UTF-8");
        var alternative = new BodyPartMultipart(new ContentType("multipart", "alternative"), "1");
        alternative.BodyParts.Add(plain);
        alternative.BodyParts.Add(html);
        Assert.Same(plain, MimeText.Select(new MailKit.MessageSummary(0) { Body = alternative }));

        var related = new BodyPartMultipart(new ContentType("multipart", "related"), "1");
        related.BodyParts.Add(html);
        related.BodyParts.Add(new BodyPartBasic(new ContentType("image", "png"), "1.2"));
        Assert.Same(html, MimeText.Select(new MailKit.MessageSummary(0) { Body = related }));

        var attachedText = Part("plain", "7bit", "UTF-8");
        attachedText.ContentDisposition = new ContentDisposition("attachment");
        var attachedMessage = new BodyPartMessage(new ContentType("message", "rfc822"), "2")
        { Body = plain, ContentDisposition = new ContentDisposition("attachment") };
        var mixed = new BodyPartMultipart(new ContentType("multipart", "mixed"), "");
        mixed.BodyParts.Add(html);
        mixed.BodyParts.Add(attachedText);
        mixed.BodyParts.Add(attachedMessage);
        Assert.Same(html, MimeText.Select(new MailKit.MessageSummary(0) { Body = mixed }));
        Assert.Null(MimeText.Select(new MailKit.MessageSummary(0) { Body = attachedMessage }));
        Assert.Null(MimeText.Select(new MailKit.MessageSummary(0) { Body = attachedText }));
    }

    [Theory]
    [InlineData("base64", "not!base64")]
    [InlineData("base64", "aGVsbG8")]
    [InlineData("base64", "====")]
    [InlineData("quoted-printable", "bad=ZZ")]
    [InlineData("quoted-printable", "bad=")]
    public async Task MalformedTransferEncodingIsNotReportedAsSuccessfulText(string encoding, string value)
    {
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes(value));
        var result = await MimeText.DecodeAsync(Part("plain", encoding, "UTF-8"), stream, CancellationToken.None);
        Assert.Equal(ContentStatus.Malformed, result.Status);
        Assert.Null(result.Text);
    }

    [Fact]
    public async Task SupportsLegacyCharsetWithoutLossyReplacement()
    {
        using var stream = new MemoryStream([0x80]);
        var result = await MimeText.DecodeAsync(Part("plain", "8bit", "windows-1252"), stream, CancellationToken.None);
        Assert.Equal(ContentStatus.Available, result.Status);
        Assert.Equal("€", result.Text);
    }

    private static BodyPartText Part(string subtype, string transfer, string charset) => new(new ContentType("text", subtype) { Charset = charset }, "1")
    { ContentTransferEncoding = transfer };
}
