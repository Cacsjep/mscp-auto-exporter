using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace AutoExporter.AdminPlugin
{
    /// <summary>
    /// Renders the plugin help page (an embedded HTML file) in a read-only WebBrowser. The page
    /// ships next to the plugin DLL at Admin\HelpPage.html and is copied to the output by the build.
    /// This matches the help style used by the other admin plugins.
    /// </summary>
    internal sealed class HtmlHelpUserControl : UserControl
    {
        public HtmlHelpUserControl()
        {
            Dock = DockStyle.Fill;

            var browser = new WebBrowser
            {
                Dock = DockStyle.Fill,
                IsWebBrowserContextMenuEnabled = false,
                AllowNavigation = false,
                ScriptErrorsSuppressed = true,
                WebBrowserShortcutsEnabled = false,
            };

            try
            {
                var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
                var htmlPath = Path.Combine(dir, "Admin", "HelpPage.html");
                if (File.Exists(htmlPath))
                    browser.Url = new Uri(htmlPath);
                else
                    browser.DocumentText = "<html><body style='font-family:Segoe UI'>Help page not found.</body></html>";
            }
            catch (Exception ex)
            {
                browser.DocumentText = "<html><body style='font-family:Segoe UI'>Could not load help: " + ex.Message + "</body></html>";
            }

            Controls.Add(browser);
        }
    }
}
