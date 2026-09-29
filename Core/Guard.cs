using System.Diagnostics;

namespace Gravitone.Core;

/// <summary>
/// A separate process that gives the taskbar back when the dock dies without doing it itself:
/// ended from Task Manager, crashed, or killed along with every "Gravitone" process.
/// It runs <c>GravitoneGuard.exe</c>, a copy of the app host made at build time, so it has its own
/// process name; and it is started through a short-lived middle process, so it is not a child of
/// the dock and "End task" on the app group or "End process tree" does not take it down too.
/// </summary>
internal static class Guard
{
    public const string SpawnArg = "--guard-spawn";
    public const string WatchArg = "--guard";

    static string ExePath
    {
        get
        {
            var guard = Path.Combine(AppContext.BaseDirectory, "GravitoneGuard.exe");
            return File.Exists(guard) ? guard : Environment.ProcessPath!;
        }
    }

    /// <summary>Starts a guard watching this process. Blocks for a moment: call it off the UI thread.</summary>
    public static Process? Start()
    {
        try
        {
            using var self = Process.GetCurrentProcess();
            var psi = new ProcessStartInfo(ExePath) { UseShellExecute = false, CreateNoWindow = true };
            psi.ArgumentList.Add(SpawnArg);
            psi.ArgumentList.Add(self.Id.ToString());
            psi.ArgumentList.Add(self.StartTime.Ticks.ToString());
            using var spawner = Process.Start(psi);
            if (spawner is null || !spawner.WaitForExit(15_000)) return null;
            return spawner.ExitCode > 0 ? Process.GetProcessById(spawner.ExitCode) : null;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Starting the guard");
            return null;
        }
    }

    /// <summary>The middle process: starts the guard and exits at once, returning its id.</summary>
    public static int RunSpawner(string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
            psi.ArgumentList.Add(WatchArg);
            foreach (var a in args.Skip(1)) psi.ArgumentList.Add(a);
            using var guard = Process.Start(psi);
            return guard?.Id ?? 0;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Starting the guard (intermediate process)");
            return 0;
        }
    }

    /// <summary>The guard: waits for the dock to end, then restores the taskbar if the dock left it hidden.</summary>
    public static int RunGuard(string[] args)
    {
        if (args.Length < 3 || !int.TryParse(args[1], out int pid) || !long.TryParse(args[2], out long startTicks))
            return 1;
        try
        {
            using var dock = Process.GetProcessById(pid);
            // A different start time means the id was reused: the dock is already gone.
            if (dock.StartTime.Ticks == startTicks) dock.WaitForExit();
        }
        catch (Exception)
        {
            // Already gone.
        }

        if (TaskbarState.Restore(onlyForPid: pid)) Log.Info("The guard restored the taskbar after Gravitone ended abnormally");
        return 0;
    }
}
