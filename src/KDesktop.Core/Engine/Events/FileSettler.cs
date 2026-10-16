namespace KDesktop.Core.Engine.Events;

/// <summary>
/// A new file is often still being written (a download, a render of ComfyUI, a copy): wait until its size stays the
/// same for a while and it can be opened for reading before saying it is there.
/// </summary>
public static class FileSettler
{
    /// <returns>True when the file is complete; false if it disappeared or <paramref name="max"/> passed.</returns>
    public static async Task<bool> WaitAsync(string path, TimeSpan stable, TimeSpan max, CancellationToken token)
    {
        var start = Environment.TickCount64;
        long lastSize = -1;
        var sameSince = Environment.TickCount64;
        while (Environment.TickCount64 - start < max.TotalMilliseconds)
        {
            token.ThrowIfCancellationRequested();
            long size;
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists) return false;
                size = info.Length;
            }
            catch (IOException) { size = -2; }
            catch (UnauthorizedAccessException) { size = -2; }

            var now = Environment.TickCount64;
            if (size != lastSize)
            {
                lastSize = size;
                sameSince = now;
            }
            else if (size >= 0 && now - sameSince >= stable.TotalMilliseconds && CanRead(path))
            {
                return true;
            }
            await Task.Delay(Math.Clamp((int)(stable.TotalMilliseconds / 4), 50, 500), token).ConfigureAwait(false);
        }
        return false;
    }

    private static bool CanRead(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
}
