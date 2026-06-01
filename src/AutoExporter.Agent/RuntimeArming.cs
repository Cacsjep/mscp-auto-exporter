using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace AutoExporter.Agent
{
    /// <summary>
    /// Points both the managed assembly resolver and the native (OS) DLL loader at the
    /// directories that hold the Milestone runtime. The export pipeline loads native
    /// CoreToolkits through the OS loader, which AssemblyResolve never sees, so without these
    /// on the native search path DBExporter.StartExport reports the toolkit as unreachable and
    /// surfaces it as "Recorder offline". Ported from the legacy helper.
    ///
    /// The agent ships the Milestone runtime next to its own exe (KeepMilestoneRuntime), so the
    /// application directory is always first. Installed Milestone roots are added too, for
    /// machines that have XProtect components present.
    /// </summary>
    internal static class RuntimeArming
    {
        private static string[] _searchDirs;
        private static bool _armed;

        public static void Arm()
        {
            if (_armed) return;
            _armed = true;

            _searchDirs = BuildSearchDirs();
            Log.Info("Runtime search dirs: " + string.Join(" | ", _searchDirs));

            ConfigureNativeDllSearch(_searchDirs);
            AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
        }

        private static string[] BuildSearchDirs()
        {
            var dirs = new List<string>();
            void Add(string d)
            {
                if (!string.IsNullOrEmpty(d) && Directory.Exists(d) &&
                    !dirs.Any(x => string.Equals(x, d, StringComparison.OrdinalIgnoreCase)))
                    dirs.Add(d);
            }

            // 1. The agent's own directory (Milestone runtime is copied here).
            Add(AppContext.BaseDirectory);

            // 2. Installed Milestone roots from the registry, so the agent also works where
            //    XProtect is installed to a non-default path. GisDriver ships SDK.UI.dll.
            foreach (var root in ReadMilestoneInstallRoots())
            {
                Add(root);
                Add(Path.Combine(root, "MIPDrivers", "GisDriver"));
            }

            // 3. Standard-install fallbacks (added only if present).
            var baseDir = @"C:\Program Files\Milestone";
            Add(Path.Combine(baseDir, "XProtect Event Server"));
            Add(Path.Combine(baseDir, "XProtect Recording Server"));
            Add(Path.Combine(baseDir, "XProtect Management Server"));
            Add(Path.Combine(baseDir, "MIPDrivers", "GisDriver"));

            return dirs.ToArray();
        }

        // Native DLL search list (Win8+/Server2012+). Kernel32 falls back to legacy rules if
        // SetDefaultDllDirectories is unavailable, in which case AddDllDirectory is a no-op.
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetDefaultDllDirectories(uint directoryFlags);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr AddDllDirectory(string newDirectory);

        private const uint LOAD_LIBRARY_SEARCH_DEFAULT_DIRS    = 0x00001000;
        private const uint LOAD_LIBRARY_SEARCH_USER_DIRS       = 0x00000400;
        private const uint LOAD_LIBRARY_SEARCH_SYSTEM32        = 0x00000800;
        private const uint LOAD_LIBRARY_SEARCH_APPLICATION_DIR = 0x00000200;

        private static void ConfigureNativeDllSearch(string[] dirs)
        {
            try
            {
                SetDefaultDllDirectories(
                    LOAD_LIBRARY_SEARCH_DEFAULT_DIRS |
                    LOAD_LIBRARY_SEARCH_USER_DIRS |
                    LOAD_LIBRARY_SEARCH_SYSTEM32 |
                    LOAD_LIBRARY_SEARCH_APPLICATION_DIR);
            }
            catch (Exception ex)
            {
                Log.Error("SetDefaultDllDirectories unavailable: " + ex.Message);
            }

            if (dirs == null) return;
            foreach (var d in dirs)
            {
                if (string.IsNullOrEmpty(d)) continue;
                try
                {
                    if (AddDllDirectory(d) == IntPtr.Zero)
                        Log.Error($"AddDllDirectory failed (gle={Marshal.GetLastWin32Error()}): {d}");
                }
                catch (Exception ex) { Log.Error($"AddDllDirectory threw for {d}: {ex.Message}"); }
            }
        }

        private static IEnumerable<string> ReadMilestoneInstallRoots()
        {
            var roots = new List<string>();
            var keys = new[]
            {
                @"SOFTWARE\VideoOS\Server",
                @"SOFTWARE\VideoOS\Recorder",
                @"SOFTWARE\VideoOS\Platform",
                @"SOFTWARE\VideoOS\EventServer",
            };
            foreach (var sub in keys)
            {
                foreach (var valueName in new[] { "InstallationPath", "InstallPath", "Path", "InstallDir" })
                {
                    var v = ReadRegistry(sub, valueName);
                    if (!string.IsNullOrEmpty(v)) roots.Add(v);
                }
            }
            return roots;
        }

        private static string ReadRegistry(string subkey, string valueName)
        {
            try
            {
                using (var baseKey = Microsoft.Win32.RegistryKey.OpenBaseKey(
                    Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64))
                using (var key = baseKey.OpenSubKey(subkey))
                {
                    return key?.GetValue(valueName) as string;
                }
            }
            catch { return null; }
        }

        private static Assembly OnAssemblyResolve(object sender, ResolveEventArgs args)
        {
            var name = new AssemblyName(args.Name).Name + ".dll";
            foreach (var dir in _searchDirs)
            {
                var path = Path.Combine(dir, name);
                if (File.Exists(path))
                {
                    try { return Assembly.LoadFrom(path); } catch { }
                }
            }
            return null;
        }
    }
}
