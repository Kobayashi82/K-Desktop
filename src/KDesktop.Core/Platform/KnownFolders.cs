using System.Runtime.InteropServices;

namespace KDesktop.Core.Platform;

/// <summary>The user's folders where they really are (Downloads moved to another drive, as the user has).</summary>
public static partial class KnownFolders
{
    private static readonly Guid DownloadsId = new("374DE290-123F-4565-9164-39C4925E467B");

    /// <summary>The Downloads folder, or null when it does not exist.</summary>
    public static string? Downloads
    {
        get
        {
            if (SHGetKnownFolderPath(DownloadsId, 0, 0, out var pointer) == 0)
            {
                try
                {
                    if (Marshal.PtrToStringUni(pointer) is { Length: > 0 } path && Directory.Exists(path)) return path;
                }
                finally
                {
                    Marshal.FreeCoTaskMem(pointer);
                }
            }
            var usual = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            return Directory.Exists(usual) ? usual : null;
        }
    }

    [LibraryImport("shell32.dll")]
    private static partial int SHGetKnownFolderPath(in Guid id, uint flags, nint token, out nint path);
}
