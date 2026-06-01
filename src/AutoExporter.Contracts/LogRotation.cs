using System.IO;

namespace AutoExporter.Contracts
{
    /// <summary>
    /// Size based log rotation shared by the agent, tray and plugin loggers. Call
    /// <see cref="RollIfNeeded"/> while holding the logger lock, just before appending. When the
    /// active file reaches the cap it is rolled to <c>name.1</c>, <c>name.2</c> and so on, oldest
    /// dropped, so the logs never grow without bound.
    /// </summary>
    public static class LogRotation
    {
        // We always log verbosely, so the cap is generous but bounded: a 10 MB active file plus 4
        // rolled backups is at most ~50 MB per log, which keeps detail without filling the disk.
        public const long DefaultMaxBytes = 10 * 1024 * 1024;  // 10 MB per file
        public const int DefaultBackups = 4;                   // name.1 .. name.4 (~50 MB total)

        public static void RollIfNeeded(string path, long maxBytes = DefaultMaxBytes, int backups = DefaultBackups)
        {
            try
            {
                var fi = new FileInfo(path);
                if (!fi.Exists || fi.Length < maxBytes) return;

                // Shift: name.(n-1) -> name.n, dropping the oldest, then active -> name.1.
                for (int i = backups; i >= 1; i--)
                {
                    var src = i == 1 ? path : path + "." + (i - 1);
                    var dst = path + "." + i;
                    if (File.Exists(dst)) File.Delete(dst);
                    if (File.Exists(src)) File.Move(src, dst);
                }
            }
            catch { /* rotation must never break logging */ }
        }
    }
}
