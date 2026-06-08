using System.Text;

namespace Jellyfin.Plugin.SubsRo;

/// <summary>
/// Decodes subtitle bytes to text. subs.ro archives are effectively always either UTF-8
/// or Windows-1250 (the dominant Romanian legacy code page, which also covers the
/// ISO-8859-2 code points for ă/â/î), so the policy is: honor a BOM, otherwise prefer a
/// strict UTF-8 decode, otherwise fall back to Windows-1250. This is deterministic and
/// avoids the false positives a generic charset detector produces on short text.
/// </summary>
public static class EncodingConverter
{
    private const int Windows1250 = 1250;

    static EncodingConverter()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>
    /// Decodes the supplied bytes to a string.
    /// </summary>
    /// <param name="data">The raw subtitle bytes.</param>
    /// <returns>The decoded text.</returns>
    public static string ToUtf8String(byte[] data)
    {
        if (data is null || data.Length == 0)
        {
            return string.Empty;
        }

        if (TryDecodeWithBom(data, out var bomDecoded))
        {
            return bomDecoded;
        }

        if (TryDecodeStrictUtf8(data, out var utf8Decoded))
        {
            return utf8Decoded;
        }

        return Encoding.GetEncoding(Windows1250).GetString(data);
    }

    private static bool TryDecodeWithBom(byte[] data, out string result)
    {
        if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
        {
            result = Encoding.UTF8.GetString(data, 3, data.Length - 3);
            return true;
        }

        if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xFE)
        {
            result = Encoding.Unicode.GetString(data, 2, data.Length - 2);
            return true;
        }

        if (data.Length >= 2 && data[0] == 0xFE && data[1] == 0xFF)
        {
            result = Encoding.BigEndianUnicode.GetString(data, 2, data.Length - 2);
            return true;
        }

        result = string.Empty;
        return false;
    }

    private static bool TryDecodeStrictUtf8(byte[] data, out string result)
    {
        try
        {
            var strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            result = strict.GetString(data);
            return true;
        }
        catch (DecoderFallbackException)
        {
            result = string.Empty;
            return false;
        }
    }
}
