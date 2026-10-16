using System.Globalization;
using System.Text;

namespace ArsivAra.Core;

/// <summary>
/// "Folds" text to make search case- and accent-insensitive:
/// "İSTANBUL", "ıstanbul", "Istanbul" → "istanbul"; "Şükrü" → "sukru"; '\' → '/'.
/// Uses a precomputed 64K table with an entry for every character, so it is very fast.
/// </summary>
public static class Folding
{
    static readonly char[] Map = BuildMap();

    static char[] BuildMap()
    {
        var map = new char[65536];
        for (int i = 0; i < map.Length; i++)
        {
            char c = (char)i;
            map[i] = char.IsSurrogate(c) ? c : FoldSlow(c);
        }
        return map;
    }

    static char FoldSlow(char c)
    {
        char f = c;
        try
        {
            // Separate the accent (ş → s + U+0327) and take the base letter only if everything after it is a combining mark.
            string d = c.ToString().Normalize(NormalizationForm.FormD);
            if (d.Length > 1 && !char.IsSurrogate(d[0]))
            {
                bool allMarks = true;
                for (int k = 1; k < d.Length; k++)
                    if (CharUnicodeInfo.GetUnicodeCategory(d[k]) != UnicodeCategory.NonSpacingMark) { allMarks = false; break; }
                if (allMarks) f = d[0];
            }
        }
        catch (ArgumentException) { }

        f = char.ToLowerInvariant(f);
        return f switch
        {
            'ı' => 'i',
            '\\' => '/',
            _ => f,
        };
    }

    public static char Fold(char c) => Map[c];

    public static int Fold(ReadOnlySpan<char> source, Span<char> destination)
    {
        for (int i = 0; i < source.Length; i++) destination[i] = Map[source[i]];
        return source.Length;
    }

    public static string Fold(string s) => string.Create(s.Length, s, static (dst, src) => Fold(src, dst));

    public static byte[] FoldToUtf8(string s) => Encoding.UTF8.GetBytes(Fold(s));
}
