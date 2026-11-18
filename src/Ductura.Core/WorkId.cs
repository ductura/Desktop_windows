using System.Globalization;
using System.Text;

namespace Ductura.Core;

/// <summary>
/// ID rada po pravilu ugovora (S-1H.1-c): prijenos <c>isWorkId</c> i <c>workIdFromPath</c> iz
/// ductura/Institution_server <c>server/contracts/work-id.ts</c> na 7b78d9b071dc2cb54392840bcdf353e68be865e3.
/// Isti ishod kao JS: rubni razmak po <c>String.prototype.trim</c>, \p{Cc}, usamljeni surogat,
/// duljina u code pointovima i jednokratno strogo dekodiranje kao <c>decodeURIComponent</c>.
/// </summary>
public static class WorkId
{
    /// <summary>Najdulji ID rada, u code pointovima.</summary>
    public const int Max = 256;

    private static readonly UTF8Encoding StrictUtf8 = new(false, throwOnInvalidBytes: true);

    /// <summary>1 do 256 code pointova, bez rubnog razmaka, kontrolnih znakova i usamljenih surogata.</summary>
    public static bool IsValid(string? value)
    {
        if (string.IsNullOrEmpty(value) || IsJsTrimmed(value[0]) || IsJsTrimmed(value[^1])) return false;
        int codePoints = 0;
        for (int i = 0; i < value.Length; i++, codePoints++)
        {
            char c = value[i];
            if (char.IsControl(c)) return false;
            if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1])) i++;
            else if (char.IsSurrogate(c)) return false;
        }
        return codePoints <= Max;
    }

    /// <summary>ID rada iz jednog sirovog (još kodiranog) segmenta putanje ili null. Dekodira točno jednom.</summary>
    public static string? FromPathSegment(string segment)
    {
        var decoded = DecodeUriComponent(segment);
        return IsValid(decoded) ? decoded : null;
    }

    // JS WhiteSpace and LineTerminator: what trim() removes. Not char.IsWhiteSpace,
    // which lacks U+FEFF; every Zs is included, as in JS.
    private static bool IsJsTrimmed(char c) =>
        c is '\t' or '\n' or '\v' or '\f' or '\r' or '\uFEFF' or '\u2028' or '\u2029'
        || CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.SpaceSeparator;

    // decodeURIComponent: raw characters stay, each run of %XX is strict UTF-8;
    // a broken escape or invalid UTF-8 is an error (null), never literal text.
    private static string? DecodeUriComponent(string s)
    {
        var result = new StringBuilder(s.Length);
        var bytes = new List<byte>();
        for (int i = 0; i < s.Length;)
        {
            if (s[i] != '%') { result.Append(s[i++]); continue; }
            bytes.Clear();
            while (i < s.Length && s[i] == '%')
            {
                if (i + 2 >= s.Length || !IsHex(s[i + 1]) || !IsHex(s[i + 2])) return null;
                bytes.Add(Convert.ToByte(s.Substring(i + 1, 2), 16));
                i += 3;
            }
            try { result.Append(StrictUtf8.GetString(bytes.ToArray())); }
            catch (DecoderFallbackException) { return null; }
        }
        return result.ToString();
    }

    private static bool IsHex(char c) => char.IsAsciiHexDigit(c);
}
