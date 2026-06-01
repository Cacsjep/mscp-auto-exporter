namespace AutoExporter.Contracts
{
    /// <summary>
    /// Milestone login modes. The agent service runs as Local System and always signs in with the
    /// credentials configured in the tray, so only credential based modes exist (no current user).
    /// </summary>
    public enum AuthMode
    {
        /// <summary>Milestone basic (local) user: username + password.</summary>
        Basic = 0,

        /// <summary>Explicit Windows/AD username + password (windows_credentials grant).</summary>
        WindowsOtherUser = 2,
    }
}
