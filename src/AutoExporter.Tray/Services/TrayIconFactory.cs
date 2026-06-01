using System;
using System.Globalization;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace AutoExporter.Tray.Services
{
    /// <summary>
    /// Builds the tray icon by rendering a Font Awesome glyph (Skia handles the OTF/CFF font
    /// reliably) onto a coloured disc, then converting to a <see cref="WindowIcon"/>.
    /// </summary>
    public static class TrayIconFactory
    {
        // Font Awesome 5 Free Solid "file-export" (U+F56E) - matches the export theme.
        private const string Glyph = "";
        private static readonly FontFamily FaSolid = new FontFamily(
            "avares://AutoExporter.Tray/Assets/Fonts/fa-solid-900.otf#Font Awesome 5 Free Solid");

        public static WindowIcon Create(bool error = false)
        {
            try { return Render(error); }
            catch
            {
                // Fall back to the bundled static icon if rendering isn't available.
                return new WindowIcon(AssetLoader.Open(new Uri("avares://AutoExporter.Tray/Assets/tray.ico")));
            }
        }

        private static WindowIcon Render(bool error)
        {
            const int size = 64;
            var rtb = new RenderTargetBitmap(new PixelSize(size, size), new Vector(96, 96));
            using (var ctx = rtb.CreateDrawingContext())
            {
                // Red disc signals a service / login error; Milestone blue otherwise.
                var disc = new SolidColorBrush(error
                    ? Color.FromRgb(229, 57, 53)   // #E53935
                    : Color.FromRgb(33, 150, 243)); // #2196F3
                ctx.DrawEllipse(disc, null, new Rect(0, 0, size, size));

                var typeface = new Typeface(FaSolid, FontStyle.Normal, FontWeight.Black);
                var text = new FormattedText(
                    Glyph, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    typeface, 34, Brushes.White);
                var origin = new Point((size - text.Width) / 2, (size - text.Height) / 2);
                ctx.DrawText(text, origin);
            }

            using var ms = new MemoryStream();
            rtb.Save(ms);
            ms.Position = 0;
            return new WindowIcon(ms);
        }
    }
}
