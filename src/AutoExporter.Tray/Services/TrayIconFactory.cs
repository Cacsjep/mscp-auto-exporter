using System;
using Avalonia.Controls;
using Avalonia.Platform;

namespace AutoExporter.Tray.Services
{
    /// <summary>
    /// Tray icon: the blue icon normally, the red one when the service or login has a problem.
    /// Both ship as .ico assets.
    /// </summary>
    public static class TrayIconFactory
    {
        public static WindowIcon Create(bool error = false)
        {
            var uri = error
                ? "avares://AutoExporter.Tray/Assets/favicon_red.ico"
                : "avares://AutoExporter.Tray/Assets/favicon_blue.ico";
            return new WindowIcon(AssetLoader.Open(new Uri(uri)));
        }
    }
}
