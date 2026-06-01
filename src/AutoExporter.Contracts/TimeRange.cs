using System;

namespace AutoExporter.Contracts
{
    /// <summary>
    /// Pure time-range math (ported from the legacy plugin). Kept free of MIP types so it can be
    /// shared and unit tested. A job exports "Last N [unit]" anchored at the trigger time.
    /// </summary>
    public static class TimeRange
    {
        public static DateTime Subtract(DateTime end, int value, string unit)
        {
            if (value <= 0) value = 1;
            switch ((unit ?? "Days").ToLowerInvariant())
            {
                case "minutes": return end.AddMinutes(-value);
                case "hours":   return end.AddHours(-value);
                case "months":  return end.AddDays(-value * 30);
                default:        return end.AddDays(-value);
            }
        }
    }
}
