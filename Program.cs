using System.Runtime.CompilerServices;
using System.Windows;
using Gravitone.Core;
using Gravitone.UI;

namespace Gravitone;

internal static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        // The guard runs this same app under another name, without any UI (see Core/Guard.cs).
        switch (args.FirstOrDefault())
        {
            case Guard.SpawnArg:
                return Guard.RunSpawner(args);
            case Guard.WatchArg:
                return Guard.RunGuard(args);
            case "--restore-taskbar":
                // Emergency switch: shows the Windows taskbar whatever state it was left in.
                TaskbarState.Restore(force: true);
                return 0;
            case AutoStart.FallbackArg:
                TaskbarState.RestoreIfOrphaned();
                return 0;
        }
        if (CommandLine.Run(args) is int code) return code;
        return RunDock();
    }

    // Kept out of Main so the guard never loads WPF.
    [MethodImpl(MethodImplOptions.NoInlining)]
    static int RunDock()
    {
        using var mutex = new Mutex(true, @"Local\Gravitone.SingleInstance", out bool first);
        if (!first) return 0;
        return new App().Run();
    }
}

internal sealed class App : Application
{
    DockWindow? _dock;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error(args.Exception, "Unhandled exception");
            args.Handled = true;
        };
        // Last chance on a fatal crash; the guard covers the cases where not even this runs.
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex) Log.Error(ex, "Fatal error");
            TaskbarState.Restore(onlyForPid: Environment.ProcessId);
        };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => TaskbarState.Restore(onlyForPid: Environment.ProcessId);

        // Started at sign-in, possibly before Explorer has its taskbar: the tray and the app bars need it.
        // (A startup task of the Store package cannot pass the argument, so a packaged start always waits.)
        if (AppPackage.IsPackaged || Environment.GetCommandLineArgs().Contains(AutoStart.AutoStartArg)) WaitForExplorer();

        Theme.Refresh();
        var config = DockConfig.LoadOrCreate();
        Loc.Init(config.Settings.Language);
        if (config.Settings.StartWithWindows) AutoStart.Ensure();

        var backdrop = new BackdropWindow();
        backdrop.Show();
        _dock = new DockWindow(config, backdrop);
        _dock.Show();
    }

    static void WaitForExplorer()
    {
        var deadline = DateTime.UtcNow.AddSeconds(45);
        while (TaskbarState.FindExplorerTray() == IntPtr.Zero && DateTime.UtcNow < deadline) Thread.Sleep(200);
        Thread.Sleep(500); // let it finish drawing its taskbar
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Always hand the reserved strip and the taskbar back.
        _dock?.ReleaseShell();
        base.OnExit(e);
    }
}
