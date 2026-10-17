using System.Text;
using MailKit;
using MimeKit;
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
            var encoding = part.ContentTransferEncoding?.ToLowerInvariant() switch
            {
                null or "7bit" => ContentEncoding.SevenBit,
                "8bit" => ContentEncoding.EightBit,
                "binary" => ContentEncoding.Binary,
                "base64" => ContentEncoding.Base64,
                "quoted-printable" => ContentEncoding.QuotedPrintable,
                _ => throw new FormatException()
            };
            ValidateEncodedText(stream, encoding, cancellationToken);
            using var content = new MimeContent(stream, encoding);
            using var decoded = new BoundedMemoryStream();
            await content.DecodeToAsync(decoded, cancellationToken);
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
        catch (Exception exception) when (exception is ArgumentException or FormatException or NotSupportedException)
        { return (null, ContentStatus.Malformed, false); }
    }

    private static void ValidateEncodedText(Stream stream, ContentEncoding encoding, CancellationToken token)
    {
        // The adapter supplies a bounded, seekable memory stream, never a network stream here.
        var start = stream.Position;
        if (stream.Length - start > MaxTransferBytes) throw new BodyLimitException();
        if (encoding is not (ContentEncoding.Base64 or ContentEncoding.QuotedPrintable)) return;
        var characters = 0;
        var padding = 0;
        int value;
        while ((value = stream.ReadByte()) != -1)
        {
            token.ThrowIfCancellationRequested();
            if (encoding == ContentEncoding.Base64)
            {
                if (value is ' ' or '\t' or '\r' or '\n') continue;
                characters++;
                if (value == '=') { if (++padding > 2) throw new FormatException(); }
                else if (padding != 0 || !(value is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '+' or '/'))
                    throw new FormatException();
            }
            else if (value == '=')
            {
                var first = stream.ReadByte();
                if (first == '\n') continue;
                if (first == '\r' && stream.ReadByte() == '\n') continue;
                if (first < 0 || !Uri.IsHexDigit((char)first)) throw new FormatException();
                var second = stream.ReadByte();
                if (second < 0 || !Uri.IsHexDigit((char)second)) throw new FormatException();
            }
        }
        if (encoding == ContentEncoding.Base64 && characters % 4 != 0) throw new FormatException();
        stream.Position = start;
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
