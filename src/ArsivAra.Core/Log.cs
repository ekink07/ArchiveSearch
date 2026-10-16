namespace ArsivAra.Core;

/// <summary>
/// Log file: "gunluk\arsivara-YYYY-MM-DD.log" in the data folder. Unexpected errors and important events are written to it;
/// the program is not affected even if the log cannot be written.
/// </summary>
public static class Log
{
    static readonly object Gate = new();

    public static string Folder => Path.Combine(AppPaths.DataDir, "gunluk");
    public static string CurrentFile => Path.Combine(Folder, $"arsivara-{DateTime.Now:yyyy-MM-dd}.log");

    public static void Info(string message) => Write("INFO", message, null);
    public static void Warn(string message, Exception? ex = null) => Write("WARN", message, ex);
    public static void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    static void Write(string level, string message, Exception? ex)
    {
        try
        {
            string line = $"{DateTime.Now:HH:mm:ss.fff} [{level}] {message}";
            if (ex != null) line += Environment.NewLine + ex;
            lock (Gate)
            {
                Directory.CreateDirectory(Folder);
                File.AppendAllText(CurrentFile, line + Environment.NewLine);
            }
        }
        catch (Exception) { /* could not write the log: ignore silently */ }
    }

    /// <summary>Deletes log files older than the given number of days.</summary>
    public static void Prune(int keepDays = 14)
    {
        try
        {
            if (!Directory.Exists(Folder)) return;
            foreach (var f in Directory.EnumerateFiles(Folder, "arsivara-*.log"))
                if (File.GetLastWriteTime(f) < DateTime.Now.AddDays(-keepDays)) File.Delete(f);
        }
        catch (Exception) { }
    }
}
