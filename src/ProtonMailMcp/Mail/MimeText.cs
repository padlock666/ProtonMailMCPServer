using System.Text;
using MailKit;
using MimeKit.Text;
using ProtonMailMcp.Contracts;

namespace ProtonMailMcp.Mail;

internal static class MimeText
{
    public const int MaxTransferBytes = 1_048_576;
    public const int MaxCharacters = 32_000;

    public static BodyPartText? Select(IMessageSummary summary)
    {
        // MailKit preserves alternative/related primary-body rules, but its top-level
        // TextBody property can also return an explicitly attached text part.
        foreach (var candidate in new[] { summary.TextBody, summary.HtmlBody })
            if (candidate is not null && IsBody(summary.Body, candidate)) return candidate;
        return null;
    }

    private static bool IsBody(BodyPart? root, BodyPartText candidate) => root switch
    {
        BodyPartText text => !text.IsAttachment && ReferenceEquals(text, candidate),
        BodyPartMultipart multipart => multipart.ContentDisposition?.IsAttachment != true &&
            multipart.BodyParts.Any(part => IsBody(part, candidate)),
        _ => false // Never descend into attached messages.
    };

    public static async Task<(string? Text, ContentStatus Status, bool Truncated)> DecodeAsync(
        BodyPartText part, Stream stream, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var encoding = part.ContentTransferEncoding?.ToLowerInvariant() ?? "7bit";
            if (encoding is not ("7bit" or "8bit" or "binary" or "base64" or "quoted-printable"))
                throw new FormatException();
            if (stream.Length - stream.Position > MaxTransferBytes) throw new BodyLimitException();
            using var decoded = new BoundedMemoryStream();
            // Share the strict transfer decoder; MimeKit's permissive decoding can silently repair malformed data.
            await AttachmentParts.DecodeAsync(stream, decoded, encoding, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            // Strict decoding exposes malformed/unknown charsets instead of claiming a successful body.
            var charset = part.ContentType.Charset ?? "us-ascii";
            var decoder = CodePagesEncodingProvider.Instance.GetEncoding(charset, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)
                ?? Encoding.GetEncoding(charset, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
            var text = decoder.GetString(decoded.GetBuffer(), 0, (int)decoded.Length);
            if (part.IsHtml) text = HtmlToPlainText(text, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var length = text.Count(character => !char.IsControl(character) || character is '\n' or '\r' or '\t');
            return (Sanitize(text, MaxCharacters, preserveWhitespace: true), ContentStatus.Available, length > MaxCharacters);
        }
        catch (BodyLimitException) { return (null, ContentStatus.Oversized, false); }
        catch (MailboxException exception) when (exception.Error == MailboxError.AttachmentMalformed)
        { return (null, ContentStatus.Malformed, false); }
        catch (Exception exception) when (exception is ArgumentException or FormatException or NotSupportedException)
        { return (null, ContentStatus.Malformed, false); }
    }

    private static string HtmlToPlainText(string html, CancellationToken token)
    {
        using var reader = new StringReader(html);
        var tokenizer = new HtmlTokenizer(reader) { DecodeCharacterReferences = true };
        var output = new StringBuilder();
        string? suppressed = null;
        while (tokenizer.ReadNextToken(out var item))
        {
            token.ThrowIfCancellationRequested();
            if (item is HtmlTagToken tag)
            {
                var name = tag.Name.ToLowerInvariant();
                if (suppressed is not null)
                {
                    if (tag.IsEndTag && name == suppressed) suppressed = null;
                    continue;
                }
                if (!tag.IsEndTag && name is "script" or "style" or "head") { suppressed = name; continue; }
                if (name is "br" or "p" or "div" or "li" or "tr" or "h1" or "h2") output.Append('\n');
            }
            else if (suppressed is null && item.Kind == HtmlTokenKind.Data && item is HtmlDataToken data)
                output.Append(data.Data);
        }
        return output.ToString();
    }

    public static string? Sanitize(string? value, int limit, bool preserveWhitespace = false)
    {
        if (value is null) return null;
        var result = new StringBuilder(Math.Min(value.Length, limit));
        foreach (var character in value)
        {
            if (char.IsControl(character) && !(preserveWhitespace && character is '\n' or '\r' or '\t')) continue;
            if (result.Length == limit) break;
            result.Append(character);
        }
        // Avoid returning half of a UTF-16 surrogate pair at the output boundary.
        if (result.Length > 0 && char.IsHighSurrogate(result[^1])) result.Length--;
        return result.ToString();
    }
}
