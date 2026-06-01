using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Threading;
using AutoExporter.Contracts;
using AutoExporter.Tray.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AutoExporter.Tray.ViewModels
{
    /// <summary>
    /// Tray configuration shell: export folder + retention, the Milestone server registration, and
    /// service control. The tray never logs in itself. It saves the configuration and asks the
    /// agent service to sign in, then shows back the service's own result (agent.state). One login
    /// path, owned by the service.
    /// </summary>
    public partial class MainWindowViewModel : ObservableObject
    {
        [ObservableProperty] private string _exportFolder = "";
        [ObservableProperty] private int _maxGB;
        [ObservableProperty] private int _retentionDays;
        [ObservableProperty] private string _logLevel = "Info";

        public string[] LogLevels { get; } = { "Error", "Info", "Debug" };

        // Left navigation: 0 = General, 1 = Registration, 2 = Control.
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ShowGeneral))]
        [NotifyPropertyChangedFor(nameof(ShowRegistration))]
        [NotifyPropertyChangedFor(nameof(ShowControl))]
        private int _selectedNav;

        public bool ShowGeneral => SelectedNav == 0;
        public bool ShowRegistration => SelectedNav == 1;
        public bool ShowControl => SelectedNav == 2;

        // ErrorMessage / StatusMessage are a shared channel across pages, so clear them when the
        // user switches pages to avoid showing one page's message on another.
        partial void OnSelectedNavChanged(int value)
        {
            ErrorMessage = null;
            StatusMessage = "";
        }

        [ObservableProperty] private string _serverUrl = "";
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(AuthSummary))]
        private string _username = "";
        [ObservableProperty] private string _password = "";

        // Password reveal toggle: PasswordChar '\0' shows clear text, bullet masks it.
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(PasswordChar))]
        private bool _showPassword;

        public char PasswordChar => ShowPassword ? '\0' : '•';
        [ObservableProperty] private bool _isBusy;
        [ObservableProperty] private string? _errorMessage;
        [ObservableProperty] private string _statusMessage = "";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(AuthSummary))]
        private AuthMode _authMode = AuthMode.Basic;

        // Connection display state. Once a login has succeeded (Registered), the editor collapses
        // into a read-only summary until the user clicks Change Server Registration.
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ShowConnectionEditor))]
        [NotifyPropertyChangedFor(nameof(ShowConnectionSummary))]
        private bool _isRegistered;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ShowConnectionEditor))]
        [NotifyPropertyChangedFor(nameof(ShowConnectionSummary))]
        private bool _isEditingConnection;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(StartServiceCommand))]
        [NotifyCanExecuteChangedFor(nameof(StopServiceCommand))]
        [NotifyCanExecuteChangedFor(nameof(RestartServiceCommand))]
        private string _serviceStatus = "Unknown";

        // What the service itself reports about its Milestone login (distinct from the tray's own
        // REST check above). This is how a LocalSystem / wrong-account failure becomes visible.
        [ObservableProperty] private string _agentStatus = "";
        [ObservableProperty] private bool _agentStatusIsError;
        [ObservableProperty] private bool _hasAgentStatus;

        // Always-visible error footer (shown on every page when the service has a problem).
        [ObservableProperty] private string _footerError = "";
        [ObservableProperty] private bool _hasFooterError;

        // Recording servers the service could not reach after login (DNS / hosts / firewall). Shown
        // inline on the Registration page and, right after a Connect, as a modal warning.
        [ObservableProperty] private string _recorderWarning = "";
        [ObservableProperty] private bool _hasRecorderWarning;

        /// <summary>Raised after a Connect when one or more recording servers were unreachable, so
        /// the view can show a modal. The payload is the human-readable list.</summary>
        public event Action<string>? RecorderWarningRaised;

        // Both remaining modes (Basic, Windows user) require a typed username + password.
        public bool NeedsExplicitCreds => true;

        // The editor shows while unregistered, or when the user chose to change the registration.
        public bool ShowConnectionEditor => !IsRegistered || IsEditingConnection;
        public bool ShowConnectionSummary => IsRegistered && !IsEditingConnection;

        public string AuthSummary
        {
            get
            {
                switch (AuthMode)
                {
                    case AuthMode.WindowsOtherUser: return "Windows user (" + Username + ")";
                    default: return "Basic user (" + Username + ")";
                }
            }
        }

        private bool CanStart => ServiceStatus == "Stopped";
        private bool CanStop => ServiceStatus == "Running";
        private bool CanRestart => ServiceStatus == "Running";

        public MainWindowViewModel()
        {
            Load();
            RefreshServiceStatus();
            RefreshAgentState();
            UpdateFooter();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            timer.Tick += (_, _) => { RefreshServiceStatus(); RefreshAgentState(); UpdateFooter(); };
            timer.Start();
        }

        private void Load()
        {
            var cfg = MachineConfig.Load();
            ServerUrl = cfg.ServerUrl;
            AuthMode = cfg.AuthMode;
            Username = cfg.Username;
            Password = cfg.Password;
            ExportFolder = cfg.ExportFolder;
            MaxGB = cfg.MaxGB;
            RetentionDays = cfg.RetentionDays;
            LogLevel = string.IsNullOrWhiteSpace(cfg.LogLevel) ? "Info" : cfg.LogLevel;
            IsRegistered = cfg.Registered;
            IsEditingConnection = false;
            // Land on Registration until a server is registered, then on General.
            SelectedNav = cfg.Registered ? 0 : 1;
        }

        private MachineConfig BuildConfig() => new()
        {
            ServerUrl = ServerUrl,
            AuthMode = AuthMode,
            Username = Username,
            Password = Password,
            ExportFolder = ExportFolder,
            MaxGB = MaxGB,
            RetentionDays = RetentionDays,
            LogLevel = LogLevel,
            Registered = IsRegistered,
        };

        [RelayCommand]
        private async Task Save()
        {
            var problem = ValidateGeneral();
            if (problem != null) { ErrorMessage = problem; StatusMessage = ""; return; }
            ErrorMessage = null;

            BuildConfig().Save();
            if (ServiceControl.Status() == "NotInstalled")
            {
                StatusMessage = "Configuration saved.";
                return;
            }
            StatusMessage = "Saved. Restarting service to apply changes...";
            await Task.Run(RestartServiceIfInstalled);
            StatusMessage = "Saved. Service restarted to apply the changes.";
        }

        // Validate the General settings before saving. Returns null when all good, otherwise a
        // message. Max size and retention are bounded by the editor (>= 0), so only the export
        // folder needs checking: it must be a real, absolute local or UNC path the service can use.
        private string? ValidateGeneral()
        {
            var folder = (ExportFolder ?? "").Trim();
            if (folder.Length == 0)
                return "Please set an export folder. Exports cannot run without it.";
            if (folder.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
                return "The export folder contains invalid characters.";
            if (!Path.IsPathRooted(folder))
                return "The export folder must be an absolute path, for example C:\\Exports or \\\\server\\share\\Exports.";
            return null;
        }

        // The service reads its config at startup, so any change (credentials or general
        // settings) only takes effect after a restart. Clear the stale state first so the tray
        // does not keep showing the previous login result while the service comes back up.
        private static void RestartServiceIfInstalled()
        {
            if (ServiceControl.Status() == "NotInstalled") return;
            AgentState.Clear();
            try { ServiceControl.Restart(); }
            catch (Exception ex) { Log.Error("Service restart failed", ex); }
        }

        // Reveal the connection editor again so the user can point the agent at a different server.
        [RelayCommand]
        private void ChangeRegistration()
        {
            IsEditingConnection = true;
            ErrorMessage = null;
            StatusMessage = "";
        }

        // ----- Connect: the SERVICE performs the only login -----
        //
        // There is exactly one component that logs in to Milestone: the agent service (via the
        // SDK, as Local System). The tray does not log in itself. Connect saves the credentials,
        // tells the service to (re)start, and then shows back exactly what the service reports in
        // agent.state. One login path, one source of truth.

        [RelayCommand]
        private async Task Connect()
        {
            if (string.IsNullOrWhiteSpace(ServerUrl)) { ErrorMessage = "Server URL is required."; return; }
            if (NeedsExplicitCreds && string.IsNullOrEmpty(Username))
            {
                ErrorMessage = AuthMode == AuthMode.Basic
                    ? "Username is required for Basic auth."
                    : "Username is required (use DOMAIN\\user or user@domain).";
                return;
            }
            if (NeedsExplicitCreds && string.IsNullOrEmpty(Password))
            {
                ErrorMessage = "Password is required.";
                return;
            }
            if (ServiceControl.Status() == "NotInstalled")
            {
                ErrorMessage = "The agent service is not installed. Install it first, then connect.";
                return;
            }

            ErrorMessage = null;
            StatusMessage = "";
            IsBusy = true;
            try
            {
                // Persist the credentials the service will use, clear the previous result, then
                // bounce the service so it logs in fresh with them.
                BuildConfig().Save();
                AgentState.Clear();
                StatusMessage = "Asking the service to sign in...";
                await Task.Run(() => { try { ServiceControl.Restart(); } catch (Exception ex) { Log.Error("Restart failed", ex); } });

                // Wait for the service to report its login result (it writes agent.state on every
                // attempt). Time out generously: the SDK login does a full config download.
                var state = await WaitForServiceResultAsync(TimeSpan.FromSeconds(75));
                RefreshServiceStatus();
                RefreshAgentState();
                UpdateFooter();

                if (state == null)
                {
                    ErrorMessage = "The service did not report a result in time. Open the service log for details.";
                }
                else if (state.LoggedIn)
                {
                    IsRegistered = true;
                    IsEditingConnection = false;
                    BuildConfig().Save();   // persist Registered = true
                    StatusMessage = $"Service signed in as {state.Identity}.";

                    // If the service could not reach every recording server, warn the operator now.
                    if (!string.IsNullOrWhiteSpace(state.RecorderWarnings))
                        RecorderWarningRaised?.Invoke(state.RecorderWarnings);
                }
                else
                {
                    ErrorMessage = string.IsNullOrEmpty(state.LastError)
                        ? "The service could not sign in. Open the service log for details."
                        : state.LastError;
                }
            }
            finally { IsBusy = false; }
        }

        // Poll agent.state (cleared just before the restart) until the service writes a fresh
        // result or we give up.
        private static async Task<AgentState?> WaitForServiceResultAsync(TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(1000).ConfigureAwait(true);
                var st = AgentState.Load();
                if (st.UpdatedUtc != default) return st;
            }
            return null;
        }

        // ----- Service control -----

        [RelayCommand]
        private void RefreshServiceStatus() => ServiceStatus = ServiceControl.Status();

        // Reflect what the service reported about its own Milestone login.
        private void RefreshAgentState()
        {
            var st = AgentState.Load();
            if (st.UpdatedUtc == default)
            {
                HasAgentStatus = false;
                AgentStatus = "";
                return;
            }
            HasAgentStatus = true;
            if (st.LoggedIn)
            {
                AgentStatusIsError = false;
                var who = st.AuthMode == "WindowsOtherUser" ? "Windows user" : "Basic user";
                AgentStatus = $"Service connected to Milestone as {who} '{st.LoginUser}' " +
                              $"(running as {st.Identity}).";
            }
            else
            {
                AgentStatusIsError = true;
                AgentStatus = string.IsNullOrEmpty(st.LastError)
                    ? "Service is not connected to Milestone."
                    : st.LastError;
            }

            // Recorder reachability warnings only make sense while connected.
            RecorderWarning = st.LoggedIn ? (st.RecorderWarnings ?? "") : "";
            HasRecorderWarning = !string.IsNullOrWhiteSpace(RecorderWarning);
        }

        [RelayCommand(CanExecute = nameof(CanStart))]
        private void StartService() => RunService(ServiceControl.Start, "started");

        [RelayCommand(CanExecute = nameof(CanStop))]
        private void StopService() => RunService(ServiceControl.Stop, "stopped");

        [RelayCommand(CanExecute = nameof(CanRestart))]
        private void RestartService() => RunService(ServiceControl.Restart, "restarted");

        private void RunService(Action act, string verb)
        {
            try { act(); StatusMessage = $"Service {verb}."; }
            catch (Exception ex) { StatusMessage = $"Service control failed: {ex.Message}"; }
            RefreshServiceStatus();
        }

        [RelayCommand] private void OpenServiceLog() => LogFiles.Open(LogFiles.ServiceLog);
        [RelayCommand] private void OpenTrayLog() => LogFiles.Open(LogFiles.TrayLog);

        // Open the configured export folder in Explorer so the operator can inspect the exports.
        [RelayCommand]
        private void OpenExportFolder()
        {
            var problem = Shell.OpenFolder(ExportFolder);
            if (problem != null) StatusMessage = problem;
        }

        // A single error line surfaced on every page: the service login failure, or a missing /
        // not-running service. Stays hidden when everything is healthy.
        private void UpdateFooter()
        {
            // Only genuine problems. "Stopped" is excluded: it is a normal, often intentional
            // state and would otherwise flash during the restart that follows a save.
            if (AgentStatusIsError && HasAgentStatus) { FooterError = AgentStatus; HasFooterError = true; return; }
            if (ServiceStatus == "NotInstalled") { FooterError = "The agent service is not installed."; HasFooterError = true; return; }
            HasFooterError = false;
            FooterError = "";
        }
    }
}
