using System;
using System.Collections.Generic;

namespace AutoExporter.Contracts
{
    /// <summary>
    /// One completed (or in-flight) export run. The agent appends these to a JSONL log and
    /// surfaces recent ones to the admin Status/Executions view.
    /// </summary>
    public sealed class ExecutionRecord
    {
        public Guid RunId;
        public Guid JobObjectId;
        public string JobName;
        public string AgentHostname;
        public DateTime StartedUtc;
        public DateTime FinishedUtc;
        public DateTime RangeStartUtc;
        public DateTime RangeEndUtc;
        public string Format;            // "XProtect" | "AVI"
        public string Trigger;           // "Rule" | "Manual"
        public bool Success;
        public string Outcome;           // "Success" | "Partial" | "Skipped" | "Failed"
        public string Error;
        public int CameraCount;
        public long BytesWritten;
        public string OutputFolder;
        public List<string> CameraNames = new List<string>();
        public List<string> SkippedCameras = new List<string>();
    }
}
