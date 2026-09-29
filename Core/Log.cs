namespace Gravitone.Core;

internal static class Log
{
    public static void Error(Exception ex, string? context = null) =>
        Write($"ERROR {context}: {ex}");

    public static void Info(string message) => Write($"INFO  {message}");

    const long MaxBytes = 1024 * 1024;
    static readonly object Gate = new();

    static void Write(string line)
    {
        try
        {
            lock (Gate) // the guard thread, the keyboard thread and crash handlers log too
            {
                Directory.CreateDirectory(DockConfig.Folder);
                var path = Path.Combine(DockConfig.Folder, "log.txt");
                // The dock runs for weeks: keep the current log and the previous one, 1 MB each.
                var info = new FileInfo(path);
                if (info.Exists && info.Length > MaxBytes) File.Move(path, Path.Combine(DockConfig.Folder, "log.old.txt"), overwrite: true);
                File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {line}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never take the dock down.
        }
    }
}
