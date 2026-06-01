using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using AutoExporter.Tray.Services;
using AutoExporter.Tray.ViewModels;
using FluentAvalonia.UI.Controls;

namespace AutoExporter.Tray.Views
{
    public partial class MainWindow : Window
    {
        private MainWindowViewModel _subscribedVm;

        public MainWindow()
        {
            InitializeComponent();
            WindowChromeHelper.HookDarkTitleBar(this);
            DataContextChanged += OnDataContextChanged;
        }

        // The view model is assigned after construction, so wire its modal-warning event here.
        private void OnDataContextChanged(object sender, EventArgs e)
        {
            if (_subscribedVm != null) _subscribedVm.RecorderWarningRaised -= OnRecorderWarning;
            _subscribedVm = DataContext as MainWindowViewModel;
            if (_subscribedVm != null) _subscribedVm.RecorderWarningRaised += OnRecorderWarning;
        }

        private void OnRecorderWarning(string details)
        {
            Dispatcher.UIThread.Post(async () =>
            {
                var dialog = new ContentDialog
                {
                    Title = "Recording servers not reachable",
                    Content = "The service signed in, but these recording servers could not be reached from this machine:\r\n\r\n"
                              + details
                              + "\r\n\r\nExports for cameras on these servers will fail. Check DNS, the hosts file, and the firewall.",
                    CloseButtonText = "OK",
                };
                try { await dialog.ShowAsync(); } catch { }
            });
        }

        private async void OnBrowseExportFolder(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainWindowViewModel vm) return;

            var start = string.IsNullOrWhiteSpace(vm.ExportFolder)
                ? null
                : await StorageProvider.TryGetFolderFromPathAsync(vm.ExportFolder);

            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select export folder",
                AllowMultiple = false,
                SuggestedStartLocation = start,
            });

            var picked = folders?.FirstOrDefault();
            if (picked == null) return;
            var path = picked.TryGetLocalPath();
            if (!string.IsNullOrEmpty(path)) vm.ExportFolder = path;
        }
    }
}
