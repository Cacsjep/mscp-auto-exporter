using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using AutoExporter.Contracts;

namespace AutoExporter.Agent
{
    /// <summary>
    /// Appends completed runs to a log at
    /// %ProgramData%\MSCPlugins\AutoExporter\executions.log, keeping the most recent entries.
    /// One <see cref="ExecutionCodec"/> line per record, so the admin Status view can read the
    /// same lines straight back over messaging (see <see cref="ReadRecent"/>).
    /// </summary>
    internal static class ExecutionStore
    {
        private const int GlobalCap = 1000;
        private static readonly object Gate = new object();

        private static readonly string Path = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "MSCPlugins", "AutoExporter", "executions.log");

        public static void Append(ExecutionRecord rec)
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
                    File.AppendAllText(Path, ExecutionCodec.Encode(rec) + Environment.NewLine, Encoding.UTF8);
                    TrimToCap();
                }
            }
            catch (Exception ex)
            {
                Log.Error("ExecutionStore append failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Most recent records, newest first, capped at <paramref name="max"/>. Used to answer the
        /// admin Status view's query over messaging. A run is written twice (Running then the final
        /// outcome, same RunId), so we keep only the newest line per RunId.
        /// </summary>
        public static List<ExecutionRecord> ReadRecent(int max)
        {
            var result = new List<ExecutionRecord>();
            try
            {
                lock (Gate)
                {
                    if (!File.Exists(Path)) return result;
                    var lines = File.ReadAllLines(Path);
                    var seen = new HashSet<Guid>();
                    for (int i = lines.Length - 1; i >= 0 && result.Count < max; i--)
                    {
                        var rec = ExecutionCodec.Decode(lines[i].TrimEnd('\r'));
                        if (rec == null) continue;
                        // Newest line for a RunId wins (we iterate from the end). Records without a
                        // RunId (legacy) are never deduped.
                        if (rec.RunId != Guid.Empty && !seen.Add(rec.RunId)) continue;
                        result.Add(rec);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("ExecutionStore read failed: " + ex.Message);
            }
            return result;
        }

        /// <summary>Delete the execution history (admin clicked Clear in the Executions view).</summary>
        public static void Clear()
        {
            try
            {
                lock (Gate)
                {
                    if (File.Exists(Path)) File.Delete(Path);
                }
            }
            catch (Exception ex)
            {
                Log.Error("ExecutionStore clear failed: " + ex.Message);
            }
        }

        private static void TrimToCap()
        {
            try
            {
                var lines = File.ReadAllLines(Path);
                if (lines.Length <= GlobalCap) return;
                var keep = new string[GlobalCap];
                Array.Copy(lines, lines.Length - GlobalCap, keep, 0, GlobalCap);
                File.WriteAllLines(Path, keep, Encoding.UTF8);
            }
            catch { }
        }
    }
}
