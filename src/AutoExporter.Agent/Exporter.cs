using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using AutoExporter.Contracts;
using VideoOS.Platform;
using VideoOS.Platform.Data;

namespace AutoExporter.Agent
{
    internal sealed class ExportRunResult
    {
        public bool Success;
        public string Error = "";
        public int CameraCount;
        public long BytesWritten;
        public List<string> CameraNames = new List<string>();
        public List<string> SkippedCameras = new List<string>();
    }

    /// <summary>
    /// Runs a Milestone export (DBExporter or AVIExporter) in-process inside the agent's SDK
    /// environment. Ported from the legacy AutoExporterHelper. MUST be called on an STA thread
    /// that pumps the Win32 message loop, because the recorder online status and the export media
    /// callbacks arrive on that loop. Without pumping, the recorder stays offline and StartExport
    /// fails with "Recorder offline".
    /// </summary>
    internal sealed class Exporter
    {
        private readonly Action<int, int, int, string> _onProgress;
        private readonly Func<bool> _shouldStop;

        public Exporter(Action<int, int, int, string> onProgress = null, Func<bool> shouldStop = null)
        {
            _onProgress = onProgress;
            _shouldStop = shouldStop;
        }

        public ExportRunResult Run(JobConfig job, string outputFolder, DateTime startUtc, DateTime endUtc)
        {
            var result = new ExportRunResult();
            try
            {
                var cameras = ResolveCameras(job, out var unresolved);
                Log.Info($"Resolve targets={job.Targets?.Count ?? 0} cameras={cameras.Count} unresolved/skipped={unresolved.Count}");
                if (cameras.Count == 0)
                {
                    // Nothing enabled/accessible to export. This is not a hard failure (e.g. all
                    // selected cameras are disabled); record it as a skipped run with detail.
                    Log.Info("No enabled, accessible cameras to export; nothing to do.");
                    result.Success = true;
                    result.CameraCount = 0;
                    result.SkippedCameras = unresolved;
                    return result;
                }

                Directory.CreateDirectory(outputFolder);

                // Pump so the recorder connection comes online before we probe or export.
                Log.Info("Settling: pumping message loop for recorder status");
                PumpMessages(6000);

                // DBExporter aborts the whole run with "Recorder offline" for any camera that has
                // no recordings-database entry, so drop those first.
                var exportable = FilterCamerasWithData(cameras, endUtc, out var skipped);
                result.SkippedCameras = unresolved.Concat(skipped).ToList();
                if (exportable.Count == 0)
                {
                    Log.Info("No camera has recorded data in or before the range, treating as success");
                    result.Success = true;
                    return result;
                }
                cameras = exportable;

                result.CameraNames = cameras.Select(c => c.Name).ToList();
                result.CameraCount = cameras.Count;

                Log.Info($"Start format={job.Format} cameras={cameras.Count} range={startUtc:O} to {endUtc:O}");

                bool isAvi = string.Equals(job.Format, "AVI", StringComparison.OrdinalIgnoreCase);
                bool isTimelapse = string.Equals(job.Format, "Timelapse", StringComparison.OrdinalIgnoreCase);
                string err;
                bool ok = isTimelapse
                    ? RunTimelapse(job, cameras, startUtc, endUtc, outputFolder, out err)
                    : isAvi
                        ? RunAvi(job, cameras, startUtc, endUtc, outputFolder, out err)
                        : RunDb(job, cameras, startUtc, endUtc, outputFolder, out err);

                result.BytesWritten = MeasureSize(outputFolder);
                result.Success = ok;
                result.Error = ok ? "" : err;
                Log.Info($"Done success={ok} bytes={result.BytesWritten} cameras={cameras.Count} skipped={skipped.Count}");
                return result;
            }
            catch (NoVideoInTimeSpanMIPException)
            {
                Log.Info("No video in time span, treating as success");
                result.Success = true;
                result.CameraCount = 0;
                return result;
            }
            catch (Exception ex)
            {
                Log.Error("Export fatal: " + ex);
                result.Success = false;
                result.Error = "Fatal: " + ex.Message;
                return result;
            }
        }

        // ----- Camera resolution -----

        private static List<Item> ResolveCameras(JobConfig job, out List<string> unresolved)
        {
            var result = new List<Item>();
            unresolved = new List<string>();
            var seen = new HashSet<Guid>();
            if (job.Targets == null) return result;

            // Build an ObjectId -> camera lookup of everything this session can actually see, and
            // log each one's name + id so a mismatch with the job's stored ids is obvious.
            var byId = new Dictionary<Guid, Item>();
            try
            {
                var all = new List<Item>();
                CollectCameras(Configuration.Instance.GetItemsByKind(Kind.Camera), all);
                foreach (var c in all)
                {
                    if (c?.FQID == null) continue;
                    byId[c.FQID.ObjectId] = c;
                    Log.Info($"  visible camera: '{c.Name}' id={c.FQID.ObjectId}");
                }
                Log.Info($"Agent session can see {all.Count} camera(s).");
            }
            catch (Exception ex)
            {
                Log.Error("Enumerating cameras failed: " + ex.Message);
            }

            foreach (var t in job.Targets)
            {
                if (t.ObjectId == Guid.Empty) continue;
                try
                {
                    // GetItem without a ServerId returns the camera bound to its Recording Server.
                    var item = Configuration.Instance.GetItem(t.ObjectId, Kind.Camera);
                    if (item == null) byId.TryGetValue(t.ObjectId, out item);   // fallback by id
                    if (item == null)
                    {
                        Log.Error($"Target {t.Kind}/'{t.Name}' ({t.ObjectId}) not found in this session.");
                        unresolved.Add(t.Name ?? t.ObjectId.ToString());
                        continue;
                    }

                    bool isGroup = item.FQID != null && item.FQID.FolderType != FolderType.No;
                    if (isGroup)
                    {
                        foreach (var leaf in FlattenCameras(item))
                            if (seen.Add(leaf.FQID.ObjectId)) result.Add(leaf);
                    }
                    else if (!item.Enabled)
                    {
                        // Disabled camera: skip it (do not fail the job), just note it.
                        Log.Info($"Skip disabled camera '{item.Name}'.");
                        unresolved.Add((item.Name ?? t.Name) + " (disabled)");
                    }
                    else if (seen.Add(item.FQID.ObjectId))
                    {
                        result.Add(item);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"Resolve failed for {t.Kind}/{t.ObjectId}: {ex.Message}");
                }
            }
            return result;
        }

        // Flatten a camera-kind item hierarchy (folders + cameras) into leaf cameras.
        private static void CollectCameras(IEnumerable<Item> items, List<Item> outList)
        {
            if (items == null) return;
            foreach (var it in items)
            {
                if (it?.FQID == null) continue;
                if (it.FQID.FolderType != FolderType.No)
                {
                    try { CollectCameras(it.GetChildren(), outList); } catch { }
                }
                else if (it.FQID.Kind == Kind.Camera)
                {
                    outList.Add(it);
                }
            }
        }

        private static IEnumerable<Item> FlattenCameras(Item folder)
        {
            IEnumerable<Item> children = Enumerable.Empty<Item>();
            try { children = folder.GetChildren() ?? Enumerable.Empty<Item>(); }
            catch { }
            foreach (var c in children)
            {
                if (c?.FQID == null || c.FQID.Kind != Kind.Camera) continue;
                if (c.FQID.FolderType != FolderType.No)
                {
                    foreach (var grand in FlattenCameras(c)) yield return grand;
                }
                else if (c.Enabled)   // skip disabled cameras inside a group
                {
                    yield return c;
                }
            }
        }

        // ----- Recorded-data filter -----

        private List<Item> FilterCamerasWithData(List<Item> cameras, DateTime endUtc, out List<string> skippedNames)
        {
            var keep = new List<Item>();
            skippedNames = new List<string>();
            foreach (var cam in cameras)
            {
                if (HasRecordedData(cam, endUtc))
                {
                    keep.Add(cam);
                }
                else
                {
                    skippedNames.Add(cam?.Name ?? "");
                    Log.Info($"Skip, no recordings in database: '{cam?.Name}'");
                }
            }
            Log.Info($"Filter {cameras.Count} camera(s) to {keep.Count} with recorded data");
            return keep;
        }

        private static bool HasRecordedData(Item cam, DateTime endUtc)
        {
            if (cam == null) return false;
            try
            {
                // Existence check: ask for at most one recording sequence at or before the range
                // end, looking back far so a single long continuous sequence is not missed.
                var source = new SequenceDataSource(cam);
                var sequences = source.GetData(
                    endUtc,
                    TimeSpan.FromDays(3650),
                    1,
                    TimeSpan.Zero, 0,
                    DataType.SequenceTypeGuids.RecordingSequence);
                return sequences != null && sequences.Count > 0;
            }
            catch (Exception ex)
            {
                Log.Info($"Probe '{cam.Name}' returned no data: {ex.Message.Trim()}");
                return false;
            }
        }

        // ----- XProtect (DBExporter) -----

        private bool RunDb(JobConfig job, List<Item> cameras, DateTime startUtc, DateTime endUtc, string outputFolder, out string error)
        {
            error = null;
            DBExporter exporter = null;
            try
            {
                // The constructor bool is the block (.scp) database format. We use the standard
                // export format (false) and write the database plus its project file to disk. The
                // Smart Client ™ Player is deliberately not bundled (IncludePlayer left at its
                // false default): the SDK can only include the player when the export runs inside the
                // Smart Client, so a standalone service can never produce it.
                exporter = new DBExporter(false)
                {
                    ExportToDisk = true,
                    Encryption = job.Encrypt,
                    EncryptionStrength = EncryptionStrength.AES128,
                    Password = job.Password ?? "",
                    SignExport = true,
                    PreventReExport = false,
                    IncludeBookmarks = true,
                    FailOnInvalidSignature = false
                };

                exporter.Init();
                exporter.Path = outputFolder;
                exporter.ExportName = MakeSafeFileName(job.Name);
                exporter.CameraList.AddRange(cameras);

                if (job.IncludeAudio)
                {
                    var audio = cameras
                        .SelectMany(SafeRelated)
                        .Where(x => x.FQID.Kind == Kind.Microphone || x.FQID.Kind == Kind.Speaker)
                        .ToList();
                    exporter.AudioList.AddRange(audio);
                }

                if (!exporter.StartExport(startUtc, endUtc))
                {
                    error = $"{exporter.LastErrorString} ({exporter.LastError})";
                    return false;
                }

                if (!WaitForCompletion(exporter, cameras.FirstOrDefault()?.Name ?? "", 0, cameras.Count, outputFolder))
                {
                    error = StalledOrStoppedMessage();
                    return false;
                }

                if (exporter.LastError > 0)
                {
                    error = $"{exporter.LastErrorString} ({exporter.LastError})";
                    return false;
                }
                return true;
            }
            finally { SafeEnd(exporter); }
        }

        // ----- AVI (per camera) -----

        private bool RunAvi(JobConfig job, List<Item> cameras, DateTime startUtc, DateTime endUtc, string outputFolder, out string error)
        {
            error = null;
            for (int i = 0; i < cameras.Count; i++)
            {
                var cam = cameras[i];
                var name = MakeSafeFileName(cam?.Name ?? $"camera_{i}");
                var camDir = Path.Combine(outputFolder, name);
                Directory.CreateDirectory(camDir);

                Log.Info($"AVI cam {i + 1}/{cameras.Count}: '{cam?.Name}'");

                AVIExporter exporter = null;
                try
                {
                    // Path = directory, Filename = base name only. Large exports auto-split at
                    // MaxAVIFileSize because AutoSplitExportFile defaults to true.
                    exporter = new AVIExporter();
                    exporter.Init();
                    exporter.Path = camDir;
                    exporter.Filename = name + ".avi";
                    exporter.Timestamp = job.Timestamp;   // burn the recording time into the frames when requested
                    exporter.CameraList.Add(cam);

                    if (job.IncludeAudio)
                    {
                        var audio = SafeRelated(cam)
                            .Where(x => x.FQID.Kind == Kind.Microphone || x.FQID.Kind == Kind.Speaker)
                            .ToList();
                        exporter.AudioList.AddRange(audio);
                    }

                    if (!exporter.StartExport(startUtc, endUtc))
                    {
                        error = $"Camera '{cam?.Name}': {exporter.LastErrorString} ({exporter.LastError})";
                        return false;
                    }

                    if (!WaitForCompletion(exporter, cam?.Name ?? "", i, cameras.Count, camDir))
                    {
                        error = $"Camera '{cam?.Name}': " + StalledOrStoppedMessage();
                        return false;
                    }

                    if (exporter.LastError > 0)
                    {
                        error = $"Camera '{cam?.Name}': {exporter.LastErrorString} ({exporter.LastError})";
                        return false;
                    }
                }
                finally { SafeEnd(exporter); }
            }
            return true;
        }

        // ----- Timelapse (per camera, FFmpeg H.264 MP4) -----

        // One MP4 per camera. We ask the recorder for the recording segments in range, sample one
        // frame per interval (recording gaps are skipped), optionally burn the time into the frame,
        // and feed the frames to the H.264 encoder. Progress is per camera, like AVI.
        private bool RunTimelapse(JobConfig job, List<Item> cameras, DateTime startUtc, DateTime endUtc, string outputFolder, out string error)
        {
            error = null;

            // The MIP sequence and frame APIs work in local time, so run the whole timelapse path in
            // local time (convert the run's UTC range once here).
            var localStart = startUtc.ToLocalTime();
            var localEnd = endUtc.ToLocalTime();
            var interval = TimeSpan.FromSeconds(Math.Max(1, job.TimelapseIntervalSeconds));
            int fps = job.TimelapseFps <= 0 ? 24 : job.TimelapseFps;

            TimeSpan dailyStart = TimeSpan.Zero, dailyEnd = TimeSpan.Zero;
            if (job.TimelapseDailyEnabled &&
                (!TryParseTimeOfDay(job.TimelapseDailyStart, out dailyStart) ||
                 !TryParseTimeOfDay(job.TimelapseDailyEnd, out dailyEnd)))
            {
                error = $"Invalid timelapse daily window ('{job.TimelapseDailyStart}' to '{job.TimelapseDailyEnd}'), expected HH:mm.";
                return false;
            }

            for (int i = 0; i < cameras.Count; i++)
            {
                if (_shouldStop != null && _shouldStop()) return false;   // recorded as Stopped upstream

                var cam = cameras[i];
                var name = MakeSafeFileName(cam?.Name ?? $"camera_{i}");
                Log.Info($"Timelapse cam {i + 1}/{cameras.Count}: '{cam?.Name}'");

                // Build the list of sample timestamps from the recording segments.
                List<DateTime> stamps;
                using (var q = new SequenceQuery(cam))
                {
                    var segments = q.GetRecordingSegments(localStart, localEnd);
                    if (job.TimelapseDailyEnabled)
                        segments = TimestampGenerator.ClipToDailyWindow(segments, dailyStart, dailyEnd);
                    stamps = TimestampGenerator.GenerateContinuous(segments, interval);
                }
                if (stamps.Count == 0)
                {
                    Log.Info($"Timelapse: '{cam?.Name}' has no footage to sample in range, skipping.");
                    continue;
                }

                var outPath = Path.Combine(outputFolder, name + ".mp4");

                using (var source = new CameraFrameSource(cam))
                {
                    // The first decodable frame fixes the encoder dimensions.
                    Bitmap firstFrame = null;
                    int firstIndex = -1;
                    for (int s = 0; s < stamps.Count; s++)
                    {
                        if (_shouldStop != null && _shouldStop()) return false;
                        var (f, _) = source.GetFrame(stamps[s]);
                        if (f != null) { firstFrame = f; firstIndex = s; break; }
                    }
                    if (firstFrame == null)
                    {
                        Log.Info($"Timelapse: '{cam?.Name}' returned no decodable frames, skipping.");
                        continue;
                    }

                    TimelapseEncoder encoder = null;
                    try
                    {
                        encoder = new TimelapseEncoder(firstFrame.Width, firstFrame.Height, fps, outPath, Log.Info);
                        if (!encoder.Start())
                        {
                            firstFrame.Dispose();
                            error = $"Camera '{cam?.Name}': could not start the timelapse encoder.";
                            return false;
                        }

                        int written = 0;
                        for (int s = firstIndex; s < stamps.Count; s++)
                        {
                            if (_shouldStop != null && _shouldStop())
                            {
                                Log.Info($"Service stopping, cancelling timelapse of '{cam?.Name}'.");
                                if (s == firstIndex) firstFrame.Dispose();
                                return false;
                            }

                            Bitmap frame = (s == firstIndex) ? firstFrame : source.GetFrame(stamps[s]).Frame;
                            if (frame == null) continue;
                            try
                            {
                                if (job.Timestamp) DrawTimestamp(frame, stamps[s]);
                                encoder.PushFrame(frame);
                                written++;
                            }
                            finally { frame.Dispose(); }

                            int pct = (int)((s + 1) * 100L / stamps.Count);
                            _onProgress?.Invoke(i, cameras.Count, pct, cam?.Name ?? "");

                            // Keep the recorder/media callbacks serviced on this STA thread.
                            System.Windows.Forms.Application.DoEvents();
                        }

                        encoder.Finish();
                        Log.Info($"Timelapse: '{cam?.Name}' wrote {written} frame(s) to '{name}.mp4'.");
                    }
                    finally { SafeDispose(encoder); }
                }
            }
            return true;
        }

        private static void SafeDispose(TimelapseEncoder encoder)
        {
            try { encoder?.Dispose(); } catch { }
        }

        private static bool TryParseTimeOfDay(string s, out TimeSpan t)
        {
            t = TimeSpan.Zero;
            if (string.IsNullOrWhiteSpace(s)) return false;
            return TimeSpan.TryParse(s.Trim(), CultureInfo.InvariantCulture, out t);
        }

        // Burns the recording time into the bottom-left of the frame, with a semi-transparent backing
        // box for legibility. Best effort: a failure here never fails the export.
        private static void DrawTimestamp(Bitmap frame, DateTime ts)
        {
            try
            {
                using (var g = Graphics.FromImage(frame))
                {
                    string text = ts.ToString("yyyy-MM-dd HH:mm:ss");
                    float fontSize = Math.Max(12f, frame.Height / 30f);
                    using (var font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel))
                    {
                        var size = g.MeasureString(text, font);
                        float pad = fontSize * 0.4f;
                        float x = pad;
                        float y = frame.Height - size.Height - pad;

                        using (var bg = new SolidBrush(Color.FromArgb(160, 0, 0, 0)))
                            g.FillRectangle(bg, x - pad / 2f, y - pad / 2f, size.Width + pad, size.Height + pad);

                        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                        using (var fg = new SolidBrush(Color.White))
                            g.DrawString(text, font, fg, x, y);
                    }
                }
            }
            catch { }
        }

        // ----- Common -----

        // Polls to completion. Returns true when finished (Progress reached 100 or an error was
        // set), false if it STALLED (neither percent nor output bytes moved for StallLimitMs). The
        // watchdog watches both because AVI keeps writing while Progress sits flat.
        private bool WaitForCompletion(IExporter exporter, string cameraName, int cameraIndex, int total, string outputPath)
        {
            const int PollMs = 250;
            const int StallLimitMs = 10 * 60 * 1000;
            const int SizeCheckMs = 5000;

            int lastPct = -1;
            long lastSize = -1;
            int sinceAdvanceMs = 0;
            int sinceSizeCheckMs = SizeCheckMs;

            while (true)
            {
                // Service is stopping: cancel the export cleanly instead of letting the stop block
                // for up to the stall limit (which would risk the SCM hard-killing us mid-export).
                if (_shouldStop != null && _shouldStop())
                {
                    Log.Info($"Service stopping, cancelling export of '{cameraName}'.");
                    try { exporter.Cancel(); } catch { }
                    return false;
                }

                int p = exporter.Progress;
                if (p < 0) p = 0;
                _onProgress?.Invoke(cameraIndex, total, p, cameraName);
                if (p >= 100 || exporter.LastError > 0) return true;

                bool advanced = false;
                if (p > lastPct) { lastPct = p; advanced = true; }

                sinceSizeCheckMs += PollMs;
                if (sinceSizeCheckMs >= SizeCheckMs)
                {
                    sinceSizeCheckMs = 0;
                    long size = MeasureSize(outputPath);
                    if (size > lastSize) { lastSize = size; advanced = true; }
                }

                if (advanced) sinceAdvanceMs = 0;
                else sinceAdvanceMs += PollMs;

                if (sinceAdvanceMs >= StallLimitMs)
                {
                    Log.Error($"Export stalled at {p}% with no new bytes for {StallLimitMs / 1000}s, cancelling");
                    try { exporter.Cancel(); } catch { }
                    return false;
                }

                PumpMessages(PollMs);
            }
        }

        // Drains the Win32 message queue on this STA thread for roughly the given duration so the
        // SDK media and recorder-status callbacks keep being dispatched while we wait.
        private static void PumpMessages(int ms)
        {
            int start = System.Environment.TickCount;
            do
            {
                System.Windows.Forms.Application.DoEvents();
                Thread.Sleep(25);
            }
            while (unchecked(System.Environment.TickCount - start) < ms);
        }

        // WaitForCompletion returns false both when an export stalls and when the service asked us
        // to stop. Word the run error to match what actually happened.
        private string StalledOrStoppedMessage()
            => (_shouldStop != null && _shouldStop())
                ? "export stopped before it finished"
                : "export stalled (no progress and no new data), cancelled";

        private static void SafeEnd(IExporter exporter)
        {
            if (exporter == null) return;
            try { exporter.EndExport(); } catch { }
            try { exporter.Close(); } catch { }
        }

        private static IEnumerable<Item> SafeRelated(Item cam)
        {
            try { return cam?.GetRelated() ?? Enumerable.Empty<Item>(); }
            catch { return Enumerable.Empty<Item>(); }
        }

        private static long MeasureSize(string folder)
        {
            long total = 0;
            try
            {
                foreach (var f in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
                {
                    try { total += new FileInfo(f).Length; } catch { }
                }
            }
            catch { }
            return total;
        }

        private static string MakeSafeFileName(string s)
        {
            if (string.IsNullOrEmpty(s)) return "_";
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(s.Length);
            foreach (var c in s) sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            return sb.ToString();
        }
    }
}
