using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using AutoExporter.Contracts;
using VideoOS.Platform;
using VideoOS.Platform.Data;

namespace AutoExporter.Agent
{
    /// <summary>
    /// Reusable frame source for a single camera. Keeps the JPEGVideoSource alive so sequential
    /// frame retrieval skips the re-init overhead. Not thread-safe: one per camera, used on the
    /// agent's STA pump thread. Ported from the Timelapse plugin's CameraFrameSource, but decodes
    /// the JPEG bytes straight into a GDI bitmap so the headless agent needs no WPF/PresentationCore.
    /// </summary>
    internal sealed class CameraFrameSource : IDisposable
    {
        private JPEGVideoSource _source;
        private bool _disposed;

        public Item CameraItem { get; }

        public CameraFrameSource(Item cameraItem)
        {
            CameraItem = cameraItem;
            _source = new JPEGVideoSource(cameraItem);
            _source.Init();
        }

        /// <summary>
        /// Fetches the frame closest to the requested time. Returns (null, reason) if there is none.
        /// </summary>
        public (Bitmap Frame, string Error) GetFrame(DateTime requestedTime)
        {
            if (_disposed) return (null, "Source disposed");

            try
            {
                // GoTo seeks the source position, then we get the nearest frame.
                _source.GoTo(requestedTime, "");

                var data = _source.GetNearest(requestedTime) as JPEGData;
                if (data == null)
                    data = _source.GetAtOrBefore(requestedTime) as JPEGData;
                if (data == null)
                    data = _source.Get(requestedTime) as JPEGData;

                if (data == null)
                    return (null, $"No frame at {requestedTime:HH:mm:ss}");

                if (data.Bytes == null || data.Bytes.Length == 0)
                    return (null, "Empty frame");

                using (var ms = new MemoryStream(data.Bytes, false))
                    return ((Bitmap)Image.FromStream(ms), null);
            }
            catch (Exception ex)
            {
                return (null, ex.Message);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _source?.Close(); } catch { }
            _source = null;
        }
    }

    /// <summary>
    /// Wraps VideoOS.Platform.Data.SequenceDataSource with the RecordingSequence type to answer:
    /// what are the exact start/end timestamps of every recording block inside [from, to]?
    /// Times in and out are local (the agent converts the run's UTC range to local for the timelapse
    /// path). Treat as one-use; dispose when done. Ported from the Timelapse plugin's SequenceQuery.
    /// </summary>
    internal sealed class SequenceQuery : IDisposable
    {
        private const int MaxSequencesPerQuery = 10000;

        private SequenceDataSource _source;
        private readonly string _cameraName;
        private bool _disposed;

        public SequenceQuery(Item cameraItem)
        {
            _cameraName = cameraItem?.Name ?? "?";
            _source = new SequenceDataSource(cameraItem);
            _source.Init();
        }

        private static DateTime ToUtc(DateTime dt)
        {
            if (dt.Kind == DateTimeKind.Utc) return dt;
            return DateTime.SpecifyKind(dt, DateTimeKind.Local).ToUniversalTime();
        }

        private static DateTime FromUtc(DateTime utc)
        {
            if (utc.Kind == DateTimeKind.Unspecified)
                utc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
            return utc.ToLocalTime();
        }

        /// <summary>
        /// Every RecordingSequence block overlapping [from, to], clipped to that window, sorted by
        /// start. Empty if the server has nothing in range. from/to are local; results are local.
        /// </summary>
        public IReadOnlyList<RecordingSegment> GetRecordingSegments(DateTime from, DateTime to)
        {
            if (_disposed) return Array.Empty<RecordingSegment>();
            if (to <= from) return Array.Empty<RecordingSegment>();

            var fromUtc = ToUtc(from);
            var toUtc = ToUtc(to);

            try
            {
                // Anchor at 'from' but look back far enough to catch a sequence that started earlier
                // and runs into the window. maxCountBefore=1 keeps it cheap.
                var raw = _source.GetData(
                    fromUtc,
                    TimeSpan.FromDays(30), 1,
                    toUtc - fromUtc, MaxSequencesPerQuery,
                    DataType.SequenceTypeGuids.RecordingSequence);

                if (raw == null || raw.Count == 0)
                {
                    Log.Info($"Timelapse: cam '{_cameraName}' has no recordings in range.");
                    return Array.Empty<RecordingSegment>();
                }

                var result = new List<RecordingSegment>(raw.Count);
                foreach (var obj in raw)
                {
                    if (!(obj is SequenceData sd) || sd.EventSequence == null) continue;
                    var s = FromUtc(sd.EventSequence.StartDateTime);
                    var e = FromUtc(sd.EventSequence.EndDateTime);
                    if (e <= s) continue;
                    if (e <= from || s >= to) continue;

                    if (s < from) s = from;
                    if (e > to) e = to;
                    if (e <= s) continue;

                    result.Add(new RecordingSegment(s, e));
                }

                result.Sort((a, b) => a.Start.CompareTo(b.Start));
                Log.Info($"Timelapse: cam '{_cameraName}' kept {result.Count} recording segment(s) in range.");
                return result;
            }
            catch (Exception ex)
            {
                Log.Error($"Timelapse: GetRecordingSegments failed for '{_cameraName}': {ex.Message}");
                return Array.Empty<RecordingSegment>();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _source?.Close(); } catch { }
            _source = null;
        }
    }
}
