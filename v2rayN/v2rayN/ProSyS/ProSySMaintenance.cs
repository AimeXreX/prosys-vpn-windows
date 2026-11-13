using Microsoft.Win32;

namespace v2rayN.ProSyS;

/// <summary>
/// Start-up chores tied to running elevated (1.2.0+): the installer cannot close or clean up an elevated copy,
/// so it asks the app itself through command-line switches, and start-with-Windows moves from the HKCU Run key
/// (Windows never starts elevated programs from there) to a "highest privileges" scheduled task.
/// </summary>
public static class ProSySMaintenance
{
    private const string Tag = "ProSySMaintenance";
    public const string StopSwitch = "--prosys-stop";
    public const string UninstallSwitch = "--prosys-uninstall";
    private const string SetupIniFile = "prosys-setup.ini";
    private const string LegacyRunValue = "ProSySVPN";

    /// <summary>Handles the installer switches. Returns true when the process should exit right away.</summary>
    public static bool HandleCommandLine(string[] args)
    {
        var stop = args.Any(a => a.Equals(StopSwitch, StringComparison.OrdinalIgnoreCase));
        var uninstall = args.Any(a => a.Equals(UninstallSwitch, StringComparison.OrdinalIgnoreCase));
        if (!stop && !uninstall)
        {
            return false;
        }
        try
        {
            StopOtherInstances();
            if (uninstall)
            {
                AutoStartupHandler.ClearWindowsAutoRun();
                RemoveLegacyRunValue();
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog(Tag, ex);
        }
        return true;
    }

    /// <summary>
    /// Applies the installer's "start with Windows" choice and migrates the 1.1.0 Run value to a scheduled task.
    /// Runs after the config is loaded; the app is elevated here, so the task gets highest privileges.
    /// </summary>
    public static async Task ApplyStartupChoicesAsync(Config config)
    {
        try
        {
            bool? autorun = null;
            var ini = Utils.GetConfigPath(SetupIniFile);
            if (File.Exists(ini))
            {
                foreach (var line in await File.ReadAllLinesAsync(ini))
                {
                    var parts = line.Split('=', 2, StringSplitOptions.TrimEntries);
                    if (parts.Length == 2 && parts[0].Equals("autorun", StringComparison.OrdinalIgnoreCase))
                    {
                        autorun = parts[1] == "1";
                    }
                }
                File.Delete(ini);
            }
            if (RemoveLegacyRunValue())
            {
                autorun ??= true;
            }
            if (autorun == null)
            {
                return;
            }
            config.GuiItem.AutoRun = autorun.Value;
            await ConfigHandler.SaveConfig(config);
            await AutoStartupHandler.UpdateTask(config);
            Logging.SaveLog($"{Tag}: start with Windows = {autorun.Value}");
        }
        catch (Exception ex)
        {
            Logging.SaveLog(Tag, ex);
        }
    }

    private static bool RemoveLegacyRunValue()
    {
        using var key = Registry.CurrentUser.OpenSubKey(Global.AutoRunRegPath, writable: true);
        if (key?.GetValue(LegacyRunValue) == null)
        {
            return false;
        }
        key.DeleteValue(LegacyRunValue, throwOnMissingValue: false);
        return true;
    }

    /// <summary>Kills other copies of this exe and the cores started from this folder, so files can be replaced.</summary>
    private static void StopOtherInstances()
    {
        var self = Environment.ProcessId;
        var exe = Utils.GetExePath();
        var root = Utils.StartupPath();
        foreach (var name in new[] { Path.GetFileNameWithoutExtension(exe), "xray", "sing-box", "mihomo" })
        {
            foreach (var proc in Process.GetProcessesByName(name))
            {
                try
                {
                    if (proc.Id == self)
                    {
                        continue;
                    }
                    var path = proc.MainModule?.FileName;
                    if (path != null && path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    {
                        proc.Kill(entireProcessTree: true);
                        proc.WaitForExit(5000);
                    }
                }
                catch (Exception ex)
                {
                    Logging.SaveLog(Tag, ex);
                }
                finally
                {
                    proc.Dispose();
                }
            }
        }
    }
}
