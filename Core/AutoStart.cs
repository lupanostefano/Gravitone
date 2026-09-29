using Microsoft.Win32;

namespace Gravitone.Core;

/// <summary>
/// Starting Gravitone at sign-in, through the user's Run key (like any tray app). In the Store package
/// none of this applies: its registry writes go to a private copy Windows never reads, and a scheduled
/// task would point into a folder that changes with every update. There the manifest's startup task does it.
/// </summary>
internal static class AutoStart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunOnceKey = @"Software\Microsoft\Windows\CurrentVersion\RunOnce";
    // Where Task Manager's "Startup apps" page records the entries the user disabled.
    const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    const string ValueName = "Gravitone";
    const string FallbackValueName = "GravitoneRestoreTaskbar";
    public const string FallbackArg = "--restore-taskbar-if-orphaned";

    public const string AutoStartArg = "--autostart";
    const string TaskName = "Gravitone";

    static string Exe => Environment.ProcessPath!;
    static string Command => $"\"{Exe}\" {AutoStartArg}";

    public static bool IsEnabled
    {
        get
        {
            if (AppPackage.IsPackaged) return true; // the manifest's startup task, on unless the user turned it off in Windows
            try
            {
                using var run = Registry.CurrentUser.OpenSubKey(RunKey);
                if (run?.GetValue(ValueName) is not string command
                    || !string.Equals(command, Command, StringComparison.OrdinalIgnoreCase)) return false;
                using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey);
                // Odd first byte = disabled in Task Manager.
                return approved?.GetValue(ValueName) is not byte[] { Length: > 0 } flags || (flags[0] & 1) == 0;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Reading the autostart entry");
                return false;
            }
        }
    }

    /// <summary>
    /// With the "start with Windows" setting on, keeps the Run entry pointing at this copy of Gravitone
    /// (it may have moved). Leaves alone an entry the user disabled in Task Manager.
    /// </summary>
    public static void Ensure()
    {
        if (AppPackage.IsPackaged) return;
        RemoveLegacyEntries();
        try
        {
            using (var run = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (!(run.GetValue(ValueName) is string command && string.Equals(command, Command, StringComparison.OrdinalIgnoreCase)))
                {
                    run.SetValue(ValueName, Command);
                    Log.Info($"Autostart set: {Command}");
                }
            }
            EnsureLogonTask();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Setting the autostart entry");
        }
    }

    /// <summary>The start-up entries of the first versions (called WinDock) pointed at an executable that is gone: remove them.</summary>
    static void RemoveLegacyEntries()
    {
        try
        {
            using (var run = Registry.CurrentUser.OpenSubKey(RunKey, writable: true))
                run?.DeleteValue("WinDock", throwOnMissingValue: false);
            using (var runOnce = Registry.CurrentUser.OpenSubKey(RunOnceKey, writable: true))
                runOnce?.DeleteValue("WinDockRestoreTaskbar", throwOnMissingValue: false);
            using (var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey, writable: true))
                approved?.DeleteValue("WinDock", throwOnMissingValue: false);
            if (RunSchtasks("/Query /TN \"WinDock\"").Code == 0) RunSchtasks("/Delete /TN \"WinDock\" /F");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Removing the autostart entries of the first version");
        }
    }

    /// <summary>
    /// While the taskbar is hidden, a one-shot entry restores it at the next sign-in if no Gravitone
    /// hid it again by then: covers a power cut, a forced restart or a disabled autostart.
    /// Windows deletes RunOnce entries before running them.
    /// </summary>
    public static void SetRestoreFallback(bool enabled)
    {
        if (AppPackage.IsPackaged) return; // RunOnce would be virtualized too: the guard covers a dock that dies
        try
        {
            using var runOnce = Registry.CurrentUser.CreateSubKey(RunOnceKey);
            if (enabled) runOnce.SetValue(FallbackValueName, $"{Command} {FallbackArg}");
            else runOnce.DeleteValue(FallbackValueName, throwOnMissingValue: false);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Setting the sign-in taskbar restore");
        }
    }

    public static void Set(bool enabled)
    {
        if (AppPackage.IsPackaged) return;
        try
        {
            using (var run = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (enabled) run.SetValue(ValueName, Command);
                else run.DeleteValue(ValueName, throwOnMissingValue: false);
            }
            // Drop a "disabled" flag Task Manager may have left, or enabling would do nothing.
            using (var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey, writable: true))
                approved?.DeleteValue(ValueName, throwOnMissingValue: false);
            if (enabled) EnsureLogonTask();
            else RunSchtasks($"/Delete /TN \"{TaskName}\" /F");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Setting the autostart entry");
        }
    }

    /// <summary>
    /// Windows holds back <c>Run</c> entries for 10-15 seconds after sign-in. A scheduled task with a
    /// logon trigger starts at once, so the dock and the menu bar are up with the desktop.
    /// The Run entry stays as a fallback (the single-instance mutex makes the second start a no-op).
    /// </summary>
    static void EnsureLogonTask()
    {
        try
        {
            string userId = System.Security.Principal.WindowsIdentity.GetCurrent().Name;
            string xml = $"""
                <?xml version="1.0" encoding="UTF-16"?>
                <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
                  <RegistrationInfo><Description>Starts Gravitone at sign-in, without the delay of Run entries.</Description></RegistrationInfo>
                  <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>{System.Security.SecurityElement.Escape(userId)}</UserId><Delay>PT0S</Delay></LogonTrigger></Triggers>
                  <Principals><Principal id="Author"><UserId>{System.Security.SecurityElement.Escape(userId)}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal></Principals>
                  <Settings>
                    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                    <Priority>4</Priority>
                  </Settings>
                  <Actions Context="Author"><Exec><Command>{System.Security.SecurityElement.Escape(Exe)}</Command><Arguments>{AutoStartArg}</Arguments></Exec></Actions>
                </Task>
                """;
            // Already there and pointing at this copy of Gravitone: nothing to do.
            var query = RunSchtasks($"/Query /TN \"{TaskName}\" /XML");
            if (query.Code == 0 && query.Output.Contains(System.Security.SecurityElement.Escape(Exe), StringComparison.OrdinalIgnoreCase)) return;

            var file = Path.Combine(Path.GetTempPath(), "gravitone-task.xml");
            File.WriteAllText(file, xml, System.Text.Encoding.Unicode);
            var result = RunSchtasks($"/Create /TN \"{TaskName}\" /XML \"{file}\" /F");
            File.Delete(file);
            Log.Info(result.Code == 0
                ? "Sign-in scheduled task created"
                : $"Scheduled task not created (code {result.Code}): {result.Output.Trim()}");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Creating the scheduled task");
        }
    }

    static (int Code, string Output) RunSchtasks(string args)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("schtasks.exe", args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var process = System.Diagnostics.Process.Start(psi)!;
        string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit(15_000);
        return (process.ExitCode, output);
    }
}
