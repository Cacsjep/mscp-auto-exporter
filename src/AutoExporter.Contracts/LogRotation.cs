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
        public const long DefaultMaxBytes = 2 * 1024 * 1024;   // 2 MB per file
        public const int DefaultBackups = 2;                   // name.1 + name.2

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
