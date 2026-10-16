using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace ArsivAra.Core;

/// <summary>
/// UTF-8 bayrağı olmayan arşiv dosya adlarını çözer:
/// bayt dizisi geçerli UTF-8 ise UTF-8 (birçok araç bayrak koymadan UTF-8 yazar),
/// değilse eski DOS kod sayfası. Kullanıcı kültürü Türkçe ise CP857, yoksa sistemin OEM kod sayfası.
/// (Sistem yerel ayarı en-US olup kültürü tr-TR olan makinelerde OEM 437 yanlış sonuç verir.)
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

    /// <summary>Belirli bir DOS kod sayfasıyla (ör. 857) çözücü; sistemin dilinden bağımsız.</summary>
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

    // Yazma tarafı kullanılmaz; yine de tutarlı olsun diye UTF-8.
    public override int GetByteCount(char[] chars, int index, int count) => UTF8.GetByteCount(chars, index, count);
    public override int GetBytes(char[] chars, int charIndex, int charCount, byte[] bytes, int byteIndex) =>
        UTF8.GetBytes(chars, charIndex, charCount, bytes, byteIndex);
    public override int GetMaxByteCount(int charCount) => UTF8.GetMaxByteCount(charCount);
    public override int GetMaxCharCount(int byteCount) => byteCount;
}
