using System.Runtime.InteropServices;

namespace Gravitone.Core;

internal static class KnownFolders
{
    static readonly Guid DownloadsId = new("374DE290-123F-4565-9164-39C4925E467B");

    [DllImport("shell32.dll")]
    static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out IntPtr path);

    /// <summary>The user's Downloads folder, wherever it has been moved to.</summary>
    public static string Downloads
    {
        get
        {
            try
            {
                if (SHGetKnownFolderPath(DownloadsId, 0, IntPtr.Zero, out var ptr) == 0)
                {
                    try
                    {
                        return Marshal.PtrToStringUni(ptr) ?? Fallback;
                    }
                    finally
                    {
                        Marshal.FreeCoTaskMem(ptr);
                    }
                }
            }
            catch (Exception)
            {
                // Use the default location.
            }
            return Fallback;
        }
    }

    static string Fallback => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
}
