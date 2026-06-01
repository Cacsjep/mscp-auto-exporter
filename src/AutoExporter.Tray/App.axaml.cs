using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using AutoExporter.Contracts;
using AutoExporter.Tray.Services;
using AutoExporter.Tray.ViewModels;
using AutoExporter.Tray.Views;

namespace AutoExporter.Tray
{
    public partial class App : Application
    {
        private TrayIcon? _trayIcon;
        private MainWindow? _configWindow;
        private NativeMenuItem? _startItem;
        private NativeMenuItem? _stopItem;
        private NativeMenuItem? _restartItem;
        private bool _iconIsError;

        public override void Initialize() => AvaloniaXamlLoader.Load(this);

        public override void OnFrameworkInitializationCompleted()
        {
            Log.Info(Diagnostics.Banner("Auto Exporter Tray", typeof(App).Assembly));
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                // This is a tray app: it lives in the notification area and has no main window.
                // Closing the config window must NOT exit the process.
                desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

                _trayIcon = BuildTrayIcon(desktop);

                // Keep the menu item states in sync with the actual service state.
                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                timer.Tick += (_, _) => RefreshServiceState();
                timer.Start();
                RefreshServiceState();
            }

            base.OnFrameworkInitializationCompleted();
        }

        private TrayIcon BuildTrayIcon(IClassicDesktopStyleApplicationLifetime desktop)
        {
            var icon = new TrayIcon
            {
                Icon = TrayIconFactory.Create(),
                ToolTipText = "Auto Exporter Agent",
                IsVisible = true,
            };

            NativeMenuItem Item(string header, Action onClick)
            {
                var mi = new NativeMenuItem(header);
                mi.Click += (_, _) => onClick();
                return mi;
            }

            _startItem   = Item("Start service",   () => Service(ServiceControl.Start));
            _stopItem    = Item("Stop service",    () => Service(ServiceControl.Stop));
            _restartItem = Item("Restart service", () => Service(ServiceControl.Restart));

            var menu = new NativeMenu();
            menu.Add(Item("Configure…", ShowConfigWindow));
            menu.Add(new NativeMenuItemSeparator());
            menu.Add(_startItem);
            menu.Add(_stopItem);
            menu.Add(_restartItem);
            menu.Add(new NativeMenuItemSeparator());
            menu.Add(Item("Open export folder", () => Shell.OpenFolder(MachineConfig.Load().ExportFolder)));
            menu.Add(Item("Open service log", () => LogFiles.Open(LogFiles.ServiceLog)));
            menu.Add(Item("Open tray log", () => LogFiles.Open(LogFiles.TrayLog)));
            menu.Add(new NativeMenuItemSeparator());
            menu.Add(Item("Exit", () => desktop.Shutdown()));
            icon.Menu = menu;

            icon.Clicked += (_, _) => ShowConfigWindow();
            return icon;
        }

        // Enable Start only when Stopped, Stop/Restart only when Running, and reflect any
        // service / login error in the tray icon and tooltip.
        private void RefreshServiceState()
        {
            var status = ServiceControl.Status();
            var running = status == "Running";
            var stopped = status == "Stopped";

            if (_startItem != null)   _startItem.IsEnabled = stopped;
            if (_stopItem != null)    _stopItem.IsEnabled = running;
            if (_restartItem != null) _restartItem.IsEnabled = running;

            // The service writes its real Milestone login result to agent.state.
            var st = AgentState.Load();
            var loginFailed = st.UpdatedUtc != default && !st.LoggedIn;
            var error = loginFailed || status == "NotInstalled";

            if (_trayIcon != null)
            {
                _trayIcon.ToolTipText = "Auto Exporter Agent - " +
                    (loginFailed && !string.IsNullOrEmpty(st.LastError) ? st.LastError : status);

                if (error != _iconIsError)
                {
                    _iconIsError = error;
                    _trayIcon.Icon = TrayIconFactory.Create(error);
                }
            }
        }

        private void ShowConfigWindow()
        {
            if (_configWindow == null)
            {
                _configWindow = new MainWindow { DataContext = new MainWindowViewModel() };
                _configWindow.Closed += (_, _) => _configWindow = null;
                _configWindow.Show();
            }
            else
            {
                if (_configWindow.WindowState == WindowState.Minimized)
                    _configWindow.WindowState = WindowState.Normal;
                _configWindow.Activate();
            }
        }

        private void Service(Action action)
        {
            try { action(); }
            catch (Exception ex) { if (_trayIcon != null) _trayIcon.ToolTipText = "Auto Exporter Agent - " + ex.Message; }
            RefreshServiceState();
        }
    }
}
