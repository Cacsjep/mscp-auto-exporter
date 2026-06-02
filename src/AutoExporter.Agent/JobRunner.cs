using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using AutoExporter.Contracts;
using VideoOS.Platform;

namespace AutoExporter.Agent
{
    /// <summary>
    /// Runs one export job in-process: load the job config, compute the time range and output
    /// folder, prune old runs (ring storage), run the export, and record the outcome.
    /// Called on the agent's STA message-pump thread (see AgentHost).
    /// </summary>
    internal sealed class JobRunner
    {
        private readonly MilestoneSession _session;
        private readonly Func<bool> _shouldStop;

        public JobRunner(MilestoneSession session, Func<bool> shouldStop = null)
        {
            _session = session;
            _shouldStop = shouldStop;
        }

        public void Run(TriggerRequest req)
        {
            var startedUtc = DateTime.UtcNow;

            // 1. Load the job config item.
            Item jobItem;
            try { jobItem = Configuration.Instance.GetItemConfiguration(Ids.PluginId, Ids.JobKindId, req.JobObjectId); }
            catch (Exception ex) { Log.Error($"Load job {req.JobObjectId} failed: {ex.Message}"); return; }

            if (jobItem?.Properties == null)
            {
                Log.Error($"Job {req.JobObjectId} not found.");
                return;
            }

            var job = JobConfig.FromProperties(jobItem.Properties);
            if (!job.Enabled)
            {
                Log.Info($"Job '{job.Name}' is disabled, skipping.");
                return;
            }

            // 2. Time range and output folder.
            var endUtc = DateTime.UtcNow;
            var rangeStartUtc = TimeRange.Subtract(endUtc, job.RangeValue, job.RangeUnit);

            var cfg = _session.Config;
            // Defense in depth: the tray validates this, but config could be hand-edited or predate
            // that check. Refuse to run rather than write to an unexpected (relative) location.
            if (string.IsNullOrWhiteSpace(cfg.ExportFolder) || !Path.IsPathRooted(cfg.ExportFolder))
            {
                Log.Error($"Job '{job.Name}': the agent export folder is not set or is not an absolute path ('{cfg.ExportFolder}'). Set it in the tray (General).");
                return;
            }
            var outputBase = ResolveOutputBase(job, cfg);
            var stamp = endUtc.ToLocalTime().ToString("dd.MM.yyyy_HHmm");
            var outputFolder = Path.Combine(outputBase, stamp);

            Log.Info($"Run job '{job.Name}' trigger={req.TriggerSource} range={rangeStartUtc:O} to {endUtc:O} out='{outputFolder}'");

            // Tell the Event Server bridge to raise the JobStarted MIP event.
            SendEvent(req.JobObjectId, JobEventNotice.KindStarted,
                $"Trigger: {req.TriggerSource} | Range: {rangeStartUtc.ToLocalTime():g} to {endUtc.ToLocalTime():g}");

            // 3. Publish a "Running" record up front so the admin Executions view shows the run
            //    immediately (within about a second of Run now), not only when it finishes.
            var rec = new ExecutionRecord
            {
                RunId = req.RunId == Guid.Empty ? Guid.NewGuid() : req.RunId,
                JobObjectId = req.JobObjectId,
                JobName = job.Name,
                AgentHostname = System.Environment.MachineName,
                StartedUtc = startedUtc,
                RangeStartUtc = rangeStartUtc,
                RangeEndUtc = endUtc,
                Format = job.Format,
                Trigger = req.TriggerSource,
                Success = false,
                Outcome = "Running",
                OutputFolder = outputFolder,
                // Show the configured target count up front so the row is not 0 while running. The
                // real resolved count (groups expanded, disabled skipped) replaces it when finished.
                CameraCount = job.Targets.Count,
            };
            PublishRecord(rec);

            // 4. Pre-run ring cleanup using the agent-wide limits from the tray config.
            var ring = RingStorage.FromGigabytes(outputBase, cfg.MaxGB, cfg.RetentionDays);
            if (ring.IsConfigured)
            {
                try { ring.Prune(); }
                catch (Exception ex) { Log.Error("Ring prune failed: " + ex.Message); }
            }

            // 5. Run the export.
            var exporter = new Exporter(OnProgress, _shouldStop);
            var run = exporter.Run(job, outputFolder, rangeStartUtc, endUtc);

            // 6. Finalize the same record (same RunId, so it replaces the Running row) and publish.
            rec.FinishedUtc = DateTime.UtcNow;
            rec.Success = run.Success;
            rec.Outcome = ClassifyOutcome(run.Success, run.CameraCount, run.SkippedCameras?.Count ?? 0);
            rec.Error = run.Error;
            rec.CameraCount = run.CameraCount;
            rec.BytesWritten = run.BytesWritten;
            rec.CameraNames = run.CameraNames;
            rec.SkippedCameras = run.SkippedCameras;
            PublishRecord(rec);

            if (run.Success)
            {
                Log.Info($"Job '{job.Name}' {rec.Outcome}: {run.CameraCount} camera(s), {run.BytesWritten / (1024 * 1024)} MB.");
                SendEvent(req.JobObjectId, JobEventNotice.KindSucceeded,
                    $"Cameras: {run.CameraCount} | Size: {run.BytesWritten / (1024 * 1024)} MB | Folder: {outputFolder}");
            }
            else
            {
                Log.Error($"Job '{job.Name}' failed: {run.Error}");
                SendEvent(req.JobObjectId, JobEventNotice.KindFailed, "Error: " + run.Error);
            }
        }

        private void SendEvent(Guid jobObjectId, string kind, string detail)
        {
            _session.SendNotice(Messages.JobEvent,
                new JobEventNotice { JobObjectId = jobObjectId, Kind = kind, Detail = detail }.Encode());
        }

        // Persist the record and push it to the admin Executions view right away. The view merges
        // by RunId, so the Running row is replaced in place when the finished record arrives.
        private void PublishRecord(ExecutionRecord rec)
        {
            ExecutionStore.Append(rec);
            try { _session.SendNotice(Messages.ExecutionsReply, ExecutionCodec.EncodeList(new List<ExecutionRecord> { rec })); }
            catch (Exception ex) { Log.Error("Publish execution record failed: " + ex.Message); }
        }

        // Use the agent export folder with a per-job subfolder so jobs do not collide.
        private static string ResolveOutputBase(JobConfig job, MachineConfig cfg)
        {
            if (!string.IsNullOrWhiteSpace(cfg.ExportFolder))
                return Path.Combine(cfg.ExportFolder, SafeName(job.Name));
            return "";
        }

        private static string SafeName(string s)
        {
            if (string.IsNullOrEmpty(s)) return "_";
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(s.Length);
            foreach (var c in s) sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            return sb.ToString();
        }

        // Ported from the legacy ExecutionOutcome classifier.
        private static string ClassifyOutcome(bool success, int cameraCount, int skippedCount)
        {
            if (!success) return "Failed";
            if (cameraCount == 0) return "Skipped";
            return skippedCount > 0 ? "Partial" : "Success";
        }

        private int _lastCam = -1;
        private void OnProgress(int cameraIndex, int total, int pct, string cameraName)
        {
            // Log only on camera transitions to avoid flooding the log.
            if (cameraIndex != _lastCam)
            {
                _lastCam = cameraIndex;
                Log.Info($"Progress: camera {cameraIndex + 1}/{total} '{cameraName}'");
            }
        }
    }
}
