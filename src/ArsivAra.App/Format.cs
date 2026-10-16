namespace ArsivAra.App;

/// <summary>Size and date text (using the selected language's number and date format).</summary>
static class Format
{
    public static string Size(long b) => b switch
    {
        < 1024 => string.Format(Loc.I.Culture, "{0} B", b),
        < 1024 * 1024 => string.Format(Loc.I.Culture, "{0:N0} KB", b / 1024.0),
        < 1024L * 1024 * 1024 => string.Format(Loc.I.Culture, "{0:N1} MB", b / (1024.0 * 1024)),
        _ => string.Format(Loc.I.Culture, "{0:N2} GB", b / (1024.0 * 1024 * 1024)),
    };

    public static string Date(long utcTicks) => utcTicks <= 0
        ? ""
        : new DateTime(utcTicks, DateTimeKind.Utc).ToLocalTime().ToString("g", Loc.I.Culture);
}
