using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace ArsivAra.Core;

/// <summary>
/// Decodes archive file names that lack the UTF-8 flag:
/// UTF-8 if the byte sequence is valid UTF-8 (many tools write UTF-8 without setting the flag),
/// otherwise the legacy DOS code page: CP857 if the user culture is Turkish, otherwise the system OEM code page.
/// (On machines whose system locale is en-US but whose culture is tr-TR, OEM 437 gives wrong results.)
/// </summary>
public sealed class NameEncoding : Encoding
{
    static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public Encoding Legacy { get; }

    public NameEncoding(Encoding legacy) => Legacy = legacy;

    [DllImport("kernel32.dll")]
    static extern uint GetOEMCP();

    public static NameEncoding CreateDefault()
    {
        RegisterProvider(CodePagesEncodingProvider.Instance);
        int cp;
        if (CultureInfo.CurrentCulture.TwoLetterISOLanguageName == "tr" || CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "tr")
            cp = 857;
        else
        {
            try { cp = (int)GetOEMCP(); } catch { cp = 437; }
        }
        try { return new NameEncoding(GetEncoding(cp)); }
        catch { return new NameEncoding(GetEncoding(437)); }
    }

    /// <summary>Decoder for a specific DOS code page (e.g. 857), independent of the system language.</summary>
    public static NameEncoding ForCodePage(int codePage)
    {
        RegisterProvider(CodePagesEncodingProvider.Instance);
        return new NameEncoding(GetEncoding(codePage));
    }

    Encoding Pick(ReadOnlySpan<byte> bytes)
    {
        if (System.Text.Ascii.IsValid(bytes)) return StrictUtf8;
        return System.Text.Unicode.Utf8.IsValid(bytes) ? StrictUtf8 : Legacy;
    }

    public override int GetCharCount(byte[] bytes, int index, int count) =>
        Pick(bytes.AsSpan(index, count)).GetCharCount(bytes, index, count);

    public override int GetChars(byte[] bytes, int byteIndex, int byteCount, char[] chars, int charIndex) =>
        Pick(bytes.AsSpan(byteIndex, byteCount)).GetChars(bytes, byteIndex, byteCount, chars, charIndex);

    public override string GetString(byte[] bytes, int index, int count) =>
        Pick(bytes.AsSpan(index, count)).GetString(bytes, index, count);

    // The encoding side is not used; it is UTF-8 anyway, for consistency.
    public override int GetByteCount(char[] chars, int index, int count) => UTF8.GetByteCount(chars, index, count);
    public override int GetBytes(char[] chars, int charIndex, int charCount, byte[] bytes, int byteIndex) =>
        UTF8.GetBytes(chars, charIndex, charCount, bytes, byteIndex);
    public override int GetMaxByteCount(int charCount) => UTF8.GetMaxByteCount(charCount);
    public override int GetMaxCharCount(int byteCount) => byteCount;
}
