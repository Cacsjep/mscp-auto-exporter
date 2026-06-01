using System;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FontAwesome5;

namespace AutoExporter.AdminPlugin
{
    /// <summary>
    /// Renders Font Awesome glyphs to a System.Drawing.Image for the Management Client tree
    /// nodes. Ported from the community PluginIcon helper.
    /// </summary>
    internal static class PluginIcon
    {
        public static readonly System.Windows.Media.Color DefaultColor =
            System.Windows.Media.Color.FromRgb(33, 150, 243);

        public static readonly Image Fallback = CreateFallback();

        public static Image Render(EFontAwesomeIcon icon, int size = 16)
        {
            try
            {
                var rtb = RenderBitmap(icon, DefaultColor, size);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(rtb));
                // Stream intentionally kept open: Bitmap needs it for the image lifetime.
                var ms = new MemoryStream();
                encoder.Save(ms);
                ms.Position = 0;
                return new Bitmap(ms);
            }
            catch
            {
                return Fallback;
            }
        }

        private static RenderTargetBitmap RenderBitmap(EFontAwesomeIcon icon, System.Windows.Media.Color color, int size)
        {
            var awesome = new ImageAwesome
            {
                Icon = icon,
                Foreground = new SolidColorBrush(color),
                Width = size,
                Height = size
            };
            awesome.Measure(new System.Windows.Size(size, size));
            awesome.Arrange(new Rect(0, 0, size, size));

            var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(awesome);
            return rtb;
        }

        private static Image CreateFallback()
        {
            var bmp = new Bitmap(16, 16);
            using (var g = Graphics.FromImage(bmp))
                g.Clear(System.Drawing.Color.FromArgb(33, 150, 243));
            return bmp;
        }
    }
}
